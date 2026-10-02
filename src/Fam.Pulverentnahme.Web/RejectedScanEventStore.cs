using System.Text.Json;

namespace Fam.Pulverentnahme.Web;

public sealed record RejectedChargeScanRequest(
    string Reason,
    string MachineWarehouse,
    string ExpectedArticle,
    string ScannedArticle,
    string ScannedBatch);

public sealed record RejectedChargeScanEvent(
    string EventId,
    DateTimeOffset RecordedAtUtc,
    string Reason,
    string PersonnelNo,
    string PersonnelName,
    string MachineWarehouse,
    string ExpectedArticle,
    string ScannedArticle,
    string ScannedBatch);

/// <summary>
/// STAGING audit trail for rejected physical charge scans. This is WebApp-owned diagnostic/audit
/// data only; it does not write to Oxaion or Syncos and is not a booking transaction.
/// </summary>
public sealed class RejectedScanEventStore
{
    private static readonly HashSet<string> AllowedReasons = new(StringComparer.Ordinal)
    {
        "WRONG_ARTICLE",
        "CHARGE_NOT_FOUND",
        "SOURCE_ALREADY_USED",
        "INVALID_QR_FORMAT"
    };

    private readonly string _baseDirectory;
    private readonly RuntimeConfigurationService _runtime;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public RejectedScanEventStore(IWebHostEnvironment environment, RuntimeConfigurationService runtime)
    {
        _baseDirectory = Path.Combine(environment.ContentRootPath, "App_Data", "scan-events");
        _runtime = runtime;
        Directory.CreateDirectory(_baseDirectory);
        MigrateLegacyFilesToStaging(_baseDirectory);
    }

    public async Task<RejectedChargeScanEvent> SaveAsync(
        RejectedChargeScanRequest request,
        string personnelNo,
        string personnelName,
        CancellationToken ct)
    {
        var normalized = Normalize(request);
        personnelNo = Clean(personnelNo, 32, nameof(personnelNo), required: true);
        personnelName = Clean(personnelName, 160, nameof(personnelName), required: true);

        var evt = new RejectedChargeScanEvent(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            normalized.Reason,
            personnelNo,
            personnelName,
            normalized.MachineWarehouse,
            normalized.ExpectedArticle,
            normalized.ScannedArticle,
            normalized.ScannedBatch);

        var fileName = $"{evt.RecordedAtUtc:yyyyMMdd_HHmmssfff}_{evt.EventId}.json";
        var path = Path.Combine(EnvironmentDirectory(), fileName);
        var temp = path + ".tmp";

        await _writeLock.WaitAsync(ct);
        try
        {
            await using (var stream = File.Create(temp))
                await JsonSerializer.SerializeAsync(stream, evt, _json, ct);
            File.Move(temp, path, true);
        }
        finally
        {
            _writeLock.Release();
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }

        return evt;
    }

    internal static RejectedChargeScanRequest Normalize(RejectedChargeScanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reason = Clean(request.Reason, 48, nameof(request.Reason), required: true).ToUpperInvariant();
        if (!AllowedReasons.Contains(reason)) throw new ArgumentException("Unsupported rejected scan reason.", nameof(request));

        return new RejectedChargeScanRequest(
            reason,
            Clean(request.MachineWarehouse, 32, nameof(request.MachineWarehouse), required: false),
            Clean(request.ExpectedArticle, 64, nameof(request.ExpectedArticle), required: false),
            Clean(request.ScannedArticle, 64, nameof(request.ScannedArticle), required: false),
            Clean(request.ScannedBatch, 128, nameof(request.ScannedBatch), required: false));
    }

    private string EnvironmentDirectory()
    {
        var directory = Path.Combine(_baseDirectory, _runtime.EnvironmentName);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void MigrateLegacyFilesToStaging(string baseDirectory)
    {
        var staging = Path.Combine(baseDirectory, RuntimeEnvironmentNames.Staging);
        Directory.CreateDirectory(staging);
        foreach (var file in Directory.EnumerateFiles(baseDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            var target = Path.Combine(staging, Path.GetFileName(file));
            if (!File.Exists(target)) File.Move(file, target);
        }
    }

    private static string Clean(string? value, int maxLength, string field, bool required)
    {
        var clean = (value ?? "").Trim();
        if (required && clean.Length == 0) throw new ArgumentException($"{field} is required.", field);
        if (clean.Length > maxLength) throw new ArgumentException($"{field} exceeds {maxLength} characters.", field);
        if (clean.Any(char.IsControl)) throw new ArgumentException($"{field} contains control characters.", field);
        return clean;
    }
}
