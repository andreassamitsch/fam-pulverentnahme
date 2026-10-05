using System.Data;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Fam.Pulverentnahme.Web;

public sealed class PersonnelAuthenticationOptions
{
    public bool Enabled { get; set; } = true;
    public string ConnectionString { get; set; } = "";
    public int SessionMinutes { get; set; } = 480;
}

public sealed record PersonnelLoginRequest(string PersonnelNo, string Password);

internal static class PersonnelAuthenticationSession
{
    public const string PersonnelNo = "personnel:no";
    public const string PersonnelName = "personnel:name";
    public const string Environment = "personnel:environment";
}

/// <summary>
/// Reproduces the legacy SYNCOS password transformation derived from controlled test users on
/// 2026-09-03. This is intentionally server-side only. It is not a cryptographic password hash.
/// The transformation has been verified for ASCII letters and digits up to 18 characters.
/// </summary>
public static class SyncosLegacyPasswordCodec
{
    private static readonly byte[] PositionKey =
    [
        0x49, 0xFC, 0x1F, 0xD1, 0x49, 0x7B, 0xFB, 0x2E, 0x63,
        0x1B, 0x56, 0x81, 0x27, 0x3D, 0x00, 0x0C, 0x0C, 0x86
    ];

    public static string EncodeVerifiedAlphanumeric(string password) =>
        Convert.ToHexString(TransformVerifiedAlphanumeric(password));

    public static bool MatchesStoredHex(string password, string storedPassword)
    {
        if (string.IsNullOrWhiteSpace(storedPassword)) return false;

        byte[] actual;
        try { actual = Convert.FromHexString(storedPassword.Trim()); }
        catch (FormatException) { return false; }

        var expected = TransformVerifiedAlphanumeric(password);
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    internal static byte[] TransformVerifiedAlphanumeric(string password)
    {
        password ??= "";
        if (password.Length == 0) throw new ArgumentException("Passwort darf nicht leer sein.");
        if (password.Length > PositionKey.Length)
            throw new ArgumentException($"Die nachgewiesene SYNCOS-Passwortlogik ist aktuell nur bis {PositionKey.Length} Zeichen bestätigt.");
        if (!password.All(IsVerifiedCharacter))
            throw new ArgumentException("Die nachgewiesene SYNCOS-Passwortlogik ist aktuell nur für ASCII-Buchstaben und Ziffern bestätigt.");

        var result = new byte[password.Length];
        for (var i = 0; i < password.Length; i++)
        {
            var transformed = (byte)(password[i] ^ PositionKey[i]);
            result[i] = transformed is >= 0x80 and <= 0x9F ? (byte)0x3F : transformed;
        }

        return result;
    }

    private static bool IsVerifiedCharacter(char value) =>
        value is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z';
}

public sealed class PersonnelCredentialStore
{
    private readonly PersonnelAuthenticationOptions _options;
    private readonly RuntimeConfigurationService _runtime;

    public PersonnelCredentialStore(
        IOptions<PersonnelAuthenticationOptions> options,
        RuntimeConfigurationService runtime)
    {
        _options = options.Value;
        _runtime = runtime;
    }

    public bool IsConfigured =>
        _options.Enabled &&
        !string.IsNullOrWhiteSpace(_options.ConnectionString);

    public async Task<string?> ReadStoredPasswordAsync(string personnelNo, CancellationToken ct)
    {
        EnsureConfigured();
        var normalized = PersonnelService.NormalizeInput(personnelNo);

        await using var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        var schema = _runtime.SyncosSchema;
        command.CommandText = $"""
            SELECT t0.PASSWORD
              FROM [{schema}].[ITSUSER] t0
             WHERE t0.ClassID = 47
               AND t0.IsEnabled = -1
               AND t0.IsVisible = -1
               AND t0.OBJECTKEY LIKE '%' + @PersonnelNo
            """;
        command.Parameters.Add(new SqlParameter("@PersonnelNo", SqlDbType.NVarChar, 10) { Value = normalized });

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult, ct);
        if (!await reader.ReadAsync(ct)) return null;

        var first = reader.IsDBNull(0) ? null : reader.GetValue(0)?.ToString()?.Trim();
        if (await reader.ReadAsync(ct))
            throw new InvalidOperationException("SYNCOS credential lookup returned more than one active visible user for the personnel number suffix.");

        return first;
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                "Mitarbeiter-Anmeldung ist nicht konfiguriert. Bitte die lokale FAM-Konfigurationsoberfläche verwenden.");
    }
}

public sealed class PersonnelAuthenticationService
{
    private readonly PersonnelService _personnel;
    private readonly PersonnelCredentialStore _credentials;

    public PersonnelAuthenticationService(PersonnelService personnel, PersonnelCredentialStore credentials)
    {
        _personnel = personnel;
        _credentials = credentials;
    }

    public bool IsConfigured => _credentials.IsConfigured;

    public async Task<PersonnelOption?> AuthenticateAsync(string personnelNo, string password, CancellationToken ct)
    {
        var normalized = PersonnelService.NormalizeInput(personnelNo);
        if (string.IsNullOrWhiteSpace(normalized) || string.IsNullOrEmpty(password)) return null;

        _ = SyncosLegacyPasswordCodec.EncodeVerifiedAlphanumeric(password);
        var person = await _personnel.ReadExactAsync(normalized, ct);
        if (person is null) return null;

        var stored = await _credentials.ReadStoredPasswordAsync(normalized, ct);
        if (stored is null) return null;

        return SyncosLegacyPasswordCodec.MatchesStoredHex(password, stored) ? person : null;
    }
}

public sealed class PersonnelBookingAuthorizationFilter : IEndpointFilter
{
    private readonly RuntimeConfigurationService _runtime;

    public PersonnelBookingAuthorizationFilter(RuntimeConfigurationService runtime)
    {
        _runtime = runtime;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<RealMixRequest>().FirstOrDefault();
        if (request is null) return await next(context);

        var session = context.HttpContext.Session;
        var personnelNo = session.GetString(PersonnelAuthenticationSession.PersonnelNo);
        var personnelName = session.GetString(PersonnelAuthenticationSession.PersonnelName);
        var environment = session.GetString(PersonnelAuthenticationSession.Environment);

        if (!string.Equals(environment, _runtime.EnvironmentName, StringComparison.Ordinal))
        {
            session.Clear();
            return Results.Json(new
            {
                status = "ENVIRONMENT_CHANGED",
                stage = "PERSONNEL_VALIDATION",
                message = "Die Serverumgebung wurde zwischenzeitlich umgeschaltet. Bitte erneut anmelden. Es wurde keine Materialbuchung gestartet."
            }, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (string.IsNullOrWhiteSpace(personnelNo) || string.IsNullOrWhiteSpace(personnelName))
        {
            return Results.Json(new
            {
                status = "AUTH_REQUIRED",
                stage = "PERSONNEL_VALIDATION",
                message = "Bitte Mitarbeiter per NFC oder mit Personalnummer und Passwort anmelden. Es wurde keine Materialbuchung gestartet."
            }, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!string.Equals(personnelNo, request.PersonnelNo, StringComparison.Ordinal) ||
            !string.Equals(personnelName, request.PersonnelName, StringComparison.Ordinal))
        {
            return Results.Json(new
            {
                status = "AUTH_CONFLICT",
                stage = "PERSONNEL_VALIDATION",
                message = "Der angemeldete Mitarbeiter stimmt nicht mit dem Buchungsvorgang überein. Bitte erneut anmelden. Es wurde keine Materialbuchung gestartet."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }
}

public static class PersonnelAuthenticationExtensions
{
    private const string LoginRateLimitPolicy = "personnel-login";

    public static IServiceCollection AddPersonnelAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PersonnelAuthenticationOptions>(configuration.GetSection("PersonnelAuthentication"));
        services.AddSingleton<PersonnelCredentialStore>();
        services.AddSingleton<PersonnelAuthenticationService>();
        services.AddSingleton<PersonnelBookingAuthorizationFilter>();

        services.AddDistributedMemoryCache();
        var sessionMinutes = Math.Clamp(configuration.GetValue<int?>("PersonnelAuthentication:SessionMinutes") ?? 480, 5, 1440);
        services.AddSession(options =>
        {
            options.Cookie.Name = ".FamPulver.Personnel";
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.IdleTimeout = TimeSpan.FromMinutes(sessionMinutes);
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(LoginRateLimitPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });

        return services;
    }

    public static WebApplication UsePersonnelAuthentication(this WebApplication app)
    {
        app.UseSession();
        app.UseRateLimiter();
        return app;
    }

    public static IEndpointRouteBuilder MapPersonnelAuthentication(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/personnel/session", (
            HttpContext http,
            PersonnelAuthenticationService auth,
            RuntimeConfigurationService runtime) =>
        {
            var no = http.Session.GetString(PersonnelAuthenticationSession.PersonnelNo);
            var name = http.Session.GetString(PersonnelAuthenticationSession.PersonnelName);
            var environment = http.Session.GetString(PersonnelAuthenticationSession.Environment);
            if (!string.IsNullOrWhiteSpace(environment)
                && !string.Equals(environment, runtime.EnvironmentName, StringComparison.Ordinal))
            {
                http.Session.Clear();
                no = null;
                name = null;
            }

            return Results.Ok(new
            {
                authenticated = !string.IsNullOrWhiteSpace(no) && !string.IsNullOrWhiteSpace(name),
                personnelNo = no,
                fullName = name,
                environment = runtime.EnvironmentName,
                authenticationConfigured = auth.IsConfigured,
                https = http.Request.IsHttps
            });
        });

        endpoints.MapPost("/api/personnel/login", async (
            PersonnelLoginRequest request,
            HttpContext http,
            PersonnelAuthenticationService auth,
            RuntimeConfigurationService runtime,
            CancellationToken ct) =>
        {
            try
            {
                var person = await auth.AuthenticateAsync(request.PersonnelNo, request.Password, ct);
                if (person is null)
                {
                    http.Session.Clear();
                    return Results.Json(new
                    {
                        status = "AUTH_FAILED",
                        message = "Personalnummer oder Passwort ist nicht korrekt."
                    }, statusCode: StatusCodes.Status401Unauthorized);
                }

                http.Session.SetString(PersonnelAuthenticationSession.PersonnelNo, person.PersonnelNo);
                http.Session.SetString(PersonnelAuthenticationSession.PersonnelName, person.FullName);
                http.Session.SetString(PersonnelAuthenticationSession.Environment, runtime.EnvironmentName);
                return Results.Ok(new
                {
                    authenticated = true,
                    personnelNo = person.PersonnelNo,
                    fullName = person.FullName,
                    https = http.Request.IsHttps,
                    loginMethod = "PASSWORD"
                });
            }
            catch (ArgumentException)
            {
                http.Session.Clear();
                return Results.Json(new
                {
                    status = "AUTH_FAILED",
                    message = "Personalnummer oder Passwort ist nicht korrekt."
                }, statusCode: StatusCodes.Status401Unauthorized);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                http.Session.Clear();
                return Results.Json(new
                {
                    status = "AUTH_UNAVAILABLE",
                    message = "Mitarbeiter-Anmeldung kann derzeit nicht sicher geprüft werden."
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireRateLimiting(LoginRateLimitPolicy);

        endpoints.MapPost("/api/personnel/logout", (HttpContext http) =>
        {
            http.Session.Clear();
            return Results.Ok(new { authenticated = false });
        });

        return endpoints;
    }
}
