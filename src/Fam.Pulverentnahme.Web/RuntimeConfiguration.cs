using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Fam.Pulverentnahme.Web;

public static class RuntimeEnvironmentNames
{
    public const string Staging = "STAGING";
    public const string Production = "PRODUCTION";

    public static string Normalize(string? value) =>
        string.Equals((value ?? "").Trim(), Production, StringComparison.OrdinalIgnoreCase)
            ? Production
            : Staging;
}

public sealed class ServiceHostOptions
{
    public int MainPort { get; set; } = 5080;
    public int AdminPort { get; set; } = 5081;
    public string ServiceName { get; set; } = "FAMPulverentnahme";
}

public sealed record RuntimeConfigurationUpdate(
    string Environment,
    string SqlServer,
    string SqlUser,
    string? SqlPassword,
    bool SqlEncrypt,
    bool SqlTrustServerCertificate,
    string SyncosStagingDatabase,
    string SyncosProductionDatabase,
    string SyncosSchema,
    string OxaionStagingDatabase,
    string OxaionProductionDatabase,
    string OxaionStagingUrl,
    string OxaionProductionUrl,
    string OxaionFirm,
    string OxaionUser,
    string? OxaionPassword,
    bool DeveloperToolsEnabled,
    bool ConfirmProduction);

public sealed record RuntimeConfigurationView(
    bool Persisted,
    string Environment,
    string SqlServer,
    string SqlUser,
    bool SqlPasswordConfigured,
    bool SqlEncrypt,
    bool SqlTrustServerCertificate,
    string SyncosStagingDatabase,
    string SyncosProductionDatabase,
    string SyncosSchema,
    string OxaionStagingDatabase,
    string OxaionProductionDatabase,
    string OxaionStagingUrl,
    string OxaionProductionUrl,
    string OxaionFirm,
    string OxaionUser,
    bool OxaionPasswordConfigured,
    bool DeveloperToolsEnabled,
    string SelectedSyncosDatabase,
    string SelectedOxaionDatabase,
    string SelectedOxaionUrl,
    string ConfigPath);

public sealed record RuntimeConnectionTestResult(
    bool Ok,
    string Environment,
    bool SyncosSqlOk,
    string SyncosSqlMessage,
    bool OxaionSqlOk,
    string OxaionSqlMessage,
    bool OxaionHttpOk,
    string OxaionHttpMessage);

internal sealed class PersistedRuntimeConfiguration
{
    public int Version { get; set; } = 1;
    public string Environment { get; set; } = RuntimeEnvironmentNames.Staging;
    public string SqlServer { get; set; } = "";
    public string SqlUser { get; set; } = "";
    public string SqlPasswordProtected { get; set; } = "";
    public bool SqlEncrypt { get; set; } = true;
    public bool SqlTrustServerCertificate { get; set; } = true;
    public string SyncosStagingDatabase { get; set; } = "syncos_stg_102";
    public string SyncosProductionDatabase { get; set; } = "syncos_prd_102";
    public string SyncosSchema { get; set; } = "ITSDEV";
    public string OxaionStagingDatabase { get; set; } = "";
    public string OxaionProductionDatabase { get; set; } = "";
    public string OxaionStagingUrl { get; set; } = "http://oxapp.cnc-domain.fuchshofer:11118";
    public string OxaionProductionUrl { get; set; } = "http://oxapp.cnc-domain.fuchshofer:11108";
    public string OxaionFirm { get; set; } = "103";
    public string OxaionUser { get; set; } = "";
    public string OxaionPasswordProtected { get; set; } = "";
    public bool DeveloperToolsEnabled { get; set; }
}

public sealed class RuntimeConfigurationService
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FAM-Pulverentnahme-ServiceConfig-v1");
    private static readonly Regex SqlIdentifier = new("^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant);
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _configPath;
    private readonly OxaionOptions _oxaion;
    private readonly SyncosOptions _syncos;
    private readonly OxaionSqlOptions _oxaionSql;
    private readonly PersonnelAuthenticationOptions _personnelAuth;
    private readonly PrototypeOptions _prototype;
    private PersistedRuntimeConfiguration _current = new();
    private bool _persisted;

    public RuntimeConfigurationService(
        IOptions<OxaionOptions> oxaion,
        IOptions<SyncosOptions> syncos,
        IOptions<OxaionSqlOptions> oxaionSql,
        IOptions<PersonnelAuthenticationOptions> personnelAuth,
        IOptions<PrototypeOptions> prototype)
    {
        _oxaion = oxaion.Value;
        _syncos = syncos.Value;
        _oxaionSql = oxaionSql.Value;
        _personnelAuth = personnelAuth.Value;
        _prototype = prototype.Value;

        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(programData))
            programData = AppContext.BaseDirectory;
        _configPath = Path.Combine(programData, "FAM-Pulverentnahme", "service-config.json");

        LoadAndApply();
    }

    public string ConfigPath => _configPath;

    public string EnvironmentName
    {
        get
        {
            lock (_gate) return RuntimeEnvironmentNames.Normalize(_current.Environment);
        }
    }

    public string SyncosSchema
    {
        get
        {
            lock (_gate) return ValidateIdentifier(_current.SyncosSchema, nameof(_current.SyncosSchema), allowEmpty: false);
        }
    }

    public RuntimeConfigurationView GetView()
    {
        lock (_gate) return ToView(_current, _persisted);
    }

    public RuntimeConfigurationView Save(RuntimeConfigurationUpdate request)
    {
        lock (_gate)
        {
            var environment = RuntimeEnvironmentNames.Normalize(request.Environment);
            if (environment == RuntimeEnvironmentNames.Production && !request.ConfirmProduction)
                throw new ArgumentException("Produktivumschaltung muss bewusst bestätigt werden.");

            var next = new PersistedRuntimeConfiguration
            {
                Version = 1,
                Environment = environment,
                SqlServer = Clean(request.SqlServer),
                SqlUser = Clean(request.SqlUser),
                SqlPasswordProtected = string.IsNullOrEmpty(request.SqlPassword)
                    ? _current.SqlPasswordProtected
                    : Protect(request.SqlPassword),
                SqlEncrypt = request.SqlEncrypt,
                SqlTrustServerCertificate = request.SqlTrustServerCertificate,
                SyncosStagingDatabase = ValidateIdentifier(request.SyncosStagingDatabase, "Syncos STAGING database", false),
                SyncosProductionDatabase = ValidateIdentifier(request.SyncosProductionDatabase, "Syncos PRODUCTION database", false),
                SyncosSchema = ValidateIdentifier(request.SyncosSchema, "Syncos schema", false),
                OxaionStagingDatabase = ValidateIdentifier(request.OxaionStagingDatabase, "Oxaion STAGING database", true),
                OxaionProductionDatabase = ValidateIdentifier(request.OxaionProductionDatabase, "Oxaion PRODUCTION database", true),
                OxaionStagingUrl = NormalizeUrl(request.OxaionStagingUrl),
                OxaionProductionUrl = NormalizeUrl(request.OxaionProductionUrl),
                OxaionFirm = Clean(request.OxaionFirm),
                OxaionUser = Clean(request.OxaionUser),
                OxaionPasswordProtected = string.IsNullOrEmpty(request.OxaionPassword)
                    ? _current.OxaionPasswordProtected
                    : Protect(request.OxaionPassword),
                DeveloperToolsEnabled = request.DeveloperToolsEnabled
            };

            ValidateRequired(next);
            Write(next);
            _current = next;
            _persisted = true;
            Apply(next);
            return ToView(next, true);
        }
    }

    public string BuildSyncosConnectionString()
    {
        lock (_gate)
        {
            var database = SelectedSyncosDatabase(_current);
            return BuildSqlConnectionString(_current, database);
        }
    }

    public string BuildOxaionSqlConnectionString()
    {
        lock (_gate)
        {
            var database = SelectedOxaionDatabase(_current);
            if (string.IsNullOrWhiteSpace(database))
                throw new InvalidOperationException($"Oxaion SQL-Datenbank für {RuntimeEnvironmentNames.Normalize(_current.Environment)} ist nicht konfiguriert.");
            return BuildSqlConnectionString(_current, database);
        }
    }

    public async Task<RuntimeConnectionTestResult> TestCurrentAsync(OxaionClient oxaion, CancellationToken ct)
    {
        string environment;
        string syncos;
        string oxaionSql;
        string syncosSchema;
        lock (_gate)
        {
            environment = RuntimeEnvironmentNames.Normalize(_current.Environment);
            syncos = BuildSqlConnectionString(_current, SelectedSyncosDatabase(_current));
            var oxaionDb = SelectedOxaionDatabase(_current);
            if (string.IsNullOrWhiteSpace(oxaionDb))
                throw new InvalidOperationException($"Oxaion SQL-Datenbank für {environment} ist nicht konfiguriert.");
            oxaionSql = BuildSqlConnectionString(_current, oxaionDb);
            syncosSchema = ValidateIdentifier(_current.SyncosSchema, "Syncos schema", false);
        }

        var syncosResult = await TestSqlAsync(syncos, $"SELECT TOP (1) 1 FROM [{syncosSchema}].[ITSUSER]", ct);
        var oxaionStockResult = await TestSqlAsync(oxaionSql, "SELECT TOP (1) 1 FROM OXAION.LLPWEP", ct);
        var oxaionTankResult = await TestSqlAsync(
            oxaionSql,
            "SELECT TOP (1) 1 FROM OXAION.ULGSTP WHERE LGLGART = N'02'",
            ct);
        var oxaionResult = (
            Ok: oxaionStockResult.Ok && oxaionTankResult.Ok,
            Message: oxaionStockResult.Ok && oxaionTankResult.Ok
                ? "Verbindung sowie Leseberechtigungen für Lagerbestand und Tanklagerorte (ULGSTP/LGLGART=02) erfolgreich."
                : $"Lagerbestand: {oxaionStockResult.Message} Tanklagerorte: {oxaionTankResult.Message}");

        bool httpOk;
        string httpMessage;
        try
        {
            await using var session = await oxaion.ConnectAsync(ct);
            httpOk = true;
            httpMessage = "Oxaion HTTP-Anmeldung erfolgreich.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            httpOk = false;
            httpMessage = ex.Message;
        }

        return new RuntimeConnectionTestResult(
            syncosResult.Ok && oxaionResult.Ok && httpOk,
            environment,
            syncosResult.Ok,
            syncosResult.Message,
            oxaionResult.Ok,
            oxaionResult.Message,
            httpOk,
            httpMessage);
    }

    private void LoadAndApply()
    {
        lock (_gate)
        {
            if (!File.Exists(_configPath))
            {
                _current = DefaultsFromLegacyOptions();
                _persisted = false;
                return;
            }

            try
            {
                var json = File.ReadAllText(_configPath);
                _current = JsonSerializer.Deserialize<PersistedRuntimeConfiguration>(json, _json)
                    ?? throw new InvalidOperationException("Service-Konfiguration ist leer.");
                ValidateRequired(_current);
                _persisted = true;
                Apply(_current);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Gespeicherte FAM-Service-Konfiguration konnte nicht gelesen werden: {_configPath}. {ex.Message}", ex);
            }
        }
    }

    private PersistedRuntimeConfiguration DefaultsFromLegacyOptions() => new()
    {
        Environment = _oxaion.StagingOnly ? RuntimeEnvironmentNames.Staging : RuntimeEnvironmentNames.Production,
        OxaionStagingUrl = string.IsNullOrWhiteSpace(_oxaion.ServerUrl) || _oxaion.ServerUrl.Contains(":11108", StringComparison.OrdinalIgnoreCase)
            ? "http://oxapp.cnc-domain.fuchshofer:11118"
            : _oxaion.ServerUrl,
        OxaionProductionUrl = _oxaion.ServerUrl.Contains(":11108", StringComparison.OrdinalIgnoreCase)
            ? _oxaion.ServerUrl
            : "http://oxapp.cnc-domain.fuchshofer:11108",
        OxaionFirm = string.IsNullOrWhiteSpace(_oxaion.Firm) ? "103" : _oxaion.Firm,
        OxaionUser = _oxaion.User,
        DeveloperToolsEnabled = _prototype.DeveloperToolsEnabled
    };

    private void Apply(PersistedRuntimeConfiguration settings)
    {
        var env = RuntimeEnvironmentNames.Normalize(settings.Environment);
        _oxaion.ServerUrl = env == RuntimeEnvironmentNames.Staging ? settings.OxaionStagingUrl : settings.OxaionProductionUrl;
        _oxaion.User = settings.OxaionUser;
        _oxaion.Password = Unprotect(settings.OxaionPasswordProtected);
        _oxaion.Firm = settings.OxaionFirm;
        _oxaion.StagingOnly = env == RuntimeEnvironmentNames.Staging;

        var syncos = BuildSqlConnectionString(settings, SelectedSyncosDatabase(settings));
        _syncos.ConnectionString = syncos;
        _personnelAuth.ConnectionString = syncos;
        _personnelAuth.Enabled = true;

        var oxaionDatabase = SelectedOxaionDatabase(settings);
        _oxaionSql.ConnectionString = string.IsNullOrWhiteSpace(oxaionDatabase)
            ? ""
            : BuildSqlConnectionString(settings, oxaionDatabase);

        _prototype.DeveloperToolsEnabled = settings.DeveloperToolsEnabled;
    }

    private RuntimeConfigurationView ToView(PersistedRuntimeConfiguration settings, bool persisted)
    {
        var environment = RuntimeEnvironmentNames.Normalize(settings.Environment);
        return new RuntimeConfigurationView(
            persisted,
            environment,
            settings.SqlServer,
            settings.SqlUser,
            !string.IsNullOrWhiteSpace(settings.SqlPasswordProtected),
            settings.SqlEncrypt,
            settings.SqlTrustServerCertificate,
            settings.SyncosStagingDatabase,
            settings.SyncosProductionDatabase,
            settings.SyncosSchema,
            settings.OxaionStagingDatabase,
            settings.OxaionProductionDatabase,
            settings.OxaionStagingUrl,
            settings.OxaionProductionUrl,
            settings.OxaionFirm,
            settings.OxaionUser,
            !string.IsNullOrWhiteSpace(settings.OxaionPasswordProtected),
            settings.DeveloperToolsEnabled,
            SelectedSyncosDatabase(settings),
            SelectedOxaionDatabase(settings),
            environment == RuntimeEnvironmentNames.Staging ? settings.OxaionStagingUrl : settings.OxaionProductionUrl,
            _configPath);
    }

    private void Write(PersistedRuntimeConfiguration settings)
    {
        var directory = Path.GetDirectoryName(_configPath)!;
        Directory.CreateDirectory(directory);
        var temp = _configPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, _json), new UTF8Encoding(false));
        File.Move(temp, _configPath, true);
    }

    private static void ValidateRequired(PersistedRuntimeConfiguration settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SqlServer)) throw new ArgumentException("SQL Server fehlt.");
        if (string.IsNullOrWhiteSpace(settings.SqlUser)) throw new ArgumentException("SQL Benutzer fehlt.");
        if (string.IsNullOrWhiteSpace(settings.SqlPasswordProtected)) throw new ArgumentException("SQL Passwort fehlt.");
        if (string.IsNullOrWhiteSpace(settings.OxaionUser)) throw new ArgumentException("Oxaion Benutzer fehlt.");
        if (string.IsNullOrWhiteSpace(settings.OxaionPasswordProtected)) throw new ArgumentException("Oxaion Passwort fehlt.");
        if (string.IsNullOrWhiteSpace(settings.OxaionFirm)) throw new ArgumentException("Oxaion Firma fehlt.");
        _ = ValidateIdentifier(settings.SyncosStagingDatabase, "Syncos STAGING database", false);
        _ = ValidateIdentifier(settings.SyncosProductionDatabase, "Syncos PRODUCTION database", false);
        _ = ValidateIdentifier(settings.SyncosSchema, "Syncos schema", false);
        _ = ValidateIdentifier(settings.OxaionStagingDatabase, "Oxaion STAGING database", true);
        _ = ValidateIdentifier(settings.OxaionProductionDatabase, "Oxaion PRODUCTION database", true);

        var env = RuntimeEnvironmentNames.Normalize(settings.Environment);
        var selectedOxaion = SelectedOxaionDatabase(settings);
        if (string.IsNullOrWhiteSpace(selectedOxaion))
            throw new ArgumentException($"Oxaion SQL-Datenbank für {env} fehlt.");

        _ = NormalizeUrl(settings.OxaionStagingUrl);
        _ = NormalizeUrl(settings.OxaionProductionUrl);
    }

    private static string BuildSqlConnectionString(PersistedRuntimeConfiguration settings, string database)
    {
        if (string.IsNullOrWhiteSpace(database))
            throw new InvalidOperationException("SQL-Datenbank ist nicht konfiguriert.");

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = settings.SqlServer.Trim(),
            InitialCatalog = database,
            UserID = settings.SqlUser.Trim(),
            Password = Unprotect(settings.SqlPasswordProtected),
            IntegratedSecurity = false,
            PersistSecurityInfo = false,
            ConnectTimeout = 15,
            ApplicationName = "FAM Pulverentnahme"
        };
        builder["Encrypt"] = settings.SqlEncrypt;
        builder["TrustServerCertificate"] = settings.SqlTrustServerCertificate;
        return builder.ConnectionString;
    }

    private static async Task<(bool Ok, string Message)> TestSqlAsync(string connectionString, string query, CancellationToken ct)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = query;
            command.CommandTimeout = 15;
            _ = await command.ExecuteScalarAsync(ct);
            return (true, "Verbindung und Leseberechtigung erfolgreich.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, ex.Message);
        }
    }

    private static string SelectedSyncosDatabase(PersistedRuntimeConfiguration settings) =>
        RuntimeEnvironmentNames.Normalize(settings.Environment) == RuntimeEnvironmentNames.Staging
            ? settings.SyncosStagingDatabase
            : settings.SyncosProductionDatabase;

    private static string SelectedOxaionDatabase(PersistedRuntimeConfiguration settings) =>
        RuntimeEnvironmentNames.Normalize(settings.Environment) == RuntimeEnvironmentNames.Staging
            ? settings.OxaionStagingDatabase
            : settings.OxaionProductionDatabase;

    private static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Die sichere Service-Konfiguration verwendet Windows DPAPI.");
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.LocalMachine);
        return Convert.ToBase64String(bytes);
    }

    private static string Unprotect(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Die sichere Service-Konfiguration verwendet Windows DPAPI.");
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(value), Entropy, DataProtectionScope.LocalMachine);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string NormalizeUrl(string? value)
    {
        var text = Clean(value).TrimEnd('/');
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException($"Ungültige Oxaion URL: {value}");
        return text;
    }

    private static string ValidateIdentifier(string? value, string name, bool allowEmpty)
    {
        var text = Clean(value);
        if (text.Length == 0 && allowEmpty) return "";
        if (!SqlIdentifier.IsMatch(text))
            throw new ArgumentException($"{name} darf nur Buchstaben, Ziffern und Unterstrich enthalten.");
        return text;
    }

    private static string Clean(string? value) => (value ?? "").Trim();
}
