using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace Fam.Pulverentnahme.Web;

public interface ISeparatePersonnelRequest
{
    string ClientOperationId { get; }
    string PersonnelNo { get; }
    string PersonnelName { get; }
}

public sealed record TankOutRequest(
    string ClientOperationId,
    string PersonnelNo,
    string PersonnelName,
    string TankWarehouse,
    string TankWarehouseText,
    string Article,
    string ArticleText,
    string Batch,
    decimal QuantityKg,
    string TargetWarehouse,
    string TargetStorageBin,
    decimal? WeighedQuantityKg = null) : ISeparatePersonnelRequest;

public sealed record TankOutLabelPrintRequest(
    string ClientOperationId,
    string PersonnelNo,
    string PersonnelName,
    string TankOutOperationId,
    int LabelCount) : ISeparatePersonnelRequest;

public sealed record TankOutLabelReprintCandidate(
    string TankOutOperationId,
    string DocumentNo,
    DateTimeOffset CompletedAt,
    string Article,
    string ArticleText,
    string Batch,
    decimal QuantityKg,
    string TargetWarehouse,
    string TargetStorageBin,
    int SuccessfulLabelsRequested,
    string? LastPrintStatus,
    DateTimeOffset? LastPrintAt,
    bool ReprintBlocked,
    string ReprintBlockReason);

public sealed record FillNewRequest(
    string ClientOperationId,
    string PersonnelNo,
    string PersonnelName,
    string TankWarehouse,
    string TankWarehouseText,
    string Article,
    string ArticleText,
    string TargetBatch,
    DateOnly ProductionDate,
    DateOnly BookingDate,
    IReadOnlyList<AdditionalPowderSource> Sources) : ISeparatePersonnelRequest;

public sealed record FaConsumptionRequest(
    string ClientOperationId,
    string PersonnelNo,
    string PersonnelName,
    string TankWarehouse,
    string TankWarehouseText,
    string Article,
    string ArticleText,
    string TankBatch,
    decimal TankQuantityKg,
    string OrderNo,
    string PlannedMachineId,
    int MaterialPosition,
    decimal ExpectedRequiredKg,
    decimal ExpectedConsumedKg,
    int ExpectedMaterialStatus,
    decimal AdditionalConsumptionKg) : ISeparatePersonnelRequest;

public sealed record FaMaterialPositionResult(
    string OrderNo,
    int MaterialPosition,
    string Article,
    string ArticleText,
    decimal RequiredKg,
    decimal ConsumedKg,
    string Unit,
    int MaterialStatus,
    string MaterialStatusText,
    bool MkBookingAllowed,
    DateTimeOffset ReadAt);

public sealed record InventoryPosition(
    string Article,
    string ArticleText,
    string Warehouse,
    string WarehouseText,
    string StorageBin,
    string Batch,
    decimal QuantityKg,
    string Unit,
    bool NegativeStock);

public sealed record InventoryTankOverview(
    string Warehouse,
    string WarehouseText,
    string Status,
    string Message,
    IReadOnlyList<MachineStockRow> Rows,
    ArticleRecognitionColorsResult? RecognitionColors);

public sealed record InventoryOverviewResult(
    IReadOnlyList<InventoryPosition> Stock,
    IReadOnlyList<InventoryTankOverview> Tanks,
    IReadOnlyDictionary<string, ArticleRecognitionColorsResult> RecognitionColors);

public sealed class SeparateOperation
{
    public string Kind { get; set; } = "";
    public string ClientOperationId { get; set; } = "";
    public string TransactionId { get; set; } = Guid.NewGuid().ToString("N");
    public string Status { get; set; } = TransactionStatuses.Created;
    public string Stage { get; set; } = "CREATED";
    public string Message { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string RequestJson { get; set; } = "";
    public string? DocumentNo { get; set; }
    public Dictionary<string, string>? HeaderDta { get; set; }
    public List<MovementRow> LastMovements { get; set; } = [];
    public List<TransactionEvent> Events { get; set; } = [];
    public decimal? ExpectedFaConsumedKg { get; set; }
    public decimal? TargetFaConsumedKg { get; set; }
    public int? ExpectedFaMaterialStatus { get; set; }
    public string? RelatedOperationId { get; set; }

    public T ReadRequest<T>() =>
        JsonSerializer.Deserialize<T>(RequestJson, SeparateOperationStore.JsonOptions)
        ?? throw new InvalidOperationException("Stored request could not be deserialized.");
}

public sealed record SeparateOperationResponse(
    string Kind,
    string ClientOperationId,
    string TransactionId,
    string Status,
    string Stage,
    string Message,
    string? DocumentNo,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<MovementRow> Movements,
    decimal? TargetFaConsumedKg = null,
    string? RelatedOperationId = null);

public static class SeparateOperationMapping
{
    public static SeparateOperationResponse ToResponse(this SeparateOperation tx) => new(
        tx.Kind,
        tx.ClientOperationId,
        tx.TransactionId,
        tx.Status,
        tx.Stage,
        tx.Message,
        tx.DocumentNo,
        tx.UpdatedAt,
        tx.LastMovements,
        tx.TargetFaConsumedKg,
        tx.RelatedOperationId);
}

public sealed class SeparateOperationStore
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _baseDirectory;
    private readonly RuntimeConfigurationService _runtime;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public SeparateOperationStore(IWebHostEnvironment env, RuntimeConfigurationService runtime)
    {
        _baseDirectory = Path.Combine(env.ContentRootPath, "App_Data", "process-transactions");
        _runtime = runtime;
        Directory.CreateDirectory(_baseDirectory);
        MigrateLegacyFilesToStaging(_baseDirectory);
    }

    public SemaphoreSlim GetLock(string kind, string id) =>
        _locks.GetOrAdd(_runtime.EnvironmentName + ":" + kind + ":" + id, _ => new SemaphoreSlim(1, 1));

    public async Task<SeparateOperation?> GetAsync(string kind, string id, CancellationToken ct)
    {
        var path = PathFor(kind, id);
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<SeparateOperation>(stream, JsonOptions, ct);
    }

    public async Task SaveAsync(SeparateOperation tx, CancellationToken ct)
    {
        tx.UpdatedAt = DateTimeOffset.UtcNow;
        var path = PathFor(tx.Kind, tx.ClientOperationId);
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, tx, JsonOptions, ct);
        File.Move(temp, path, true);
    }

    public async Task<IReadOnlyList<SeparateOperation>> ListAsync(string kind, CancellationToken ct)
    {
        var safeKind = Safe(kind);
        if (safeKind.Length == 0) throw new ArgumentException("Invalid transaction kind.");

        var files = Directory.EnumerateFiles(EnvironmentDirectory(), safeKind + "_*.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
        var result = new List<SeparateOperation>(files.Length);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var operation = await JsonSerializer.DeserializeAsync<SeparateOperation>(stream, JsonOptions, ct);
            if (operation is not null && string.Equals(operation.Kind, kind, StringComparison.Ordinal))
                result.Add(operation);
        }

        return result
            .OrderByDescending(x => x.UpdatedAt)
            .ThenByDescending(x => x.CreatedAt)
            .ToArray();
    }

    public static string SerializeRequest<T>(T request) => JsonSerializer.Serialize(request, JsonOptions);

    private string PathFor(string kind, string id)
    {
        var k = Safe(kind);
        var i = Safe(id);
        if (k.Length == 0 || i.Length == 0) throw new ArgumentException("Invalid transaction key.");
        return Path.Combine(EnvironmentDirectory(), k + "_" + i + ".json");
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

    private static string Safe(string value) =>
        string.Concat((value ?? "").Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
}

public sealed class ProcessConflictException(string message) : Exception(message);

public sealed record TransferSpec(
    int Position,
    string BookingKey,
    string Article,
    string ArticleText,
    string FromWarehouse,
    string FromWarehouseText,
    string FromStorageBin,
    string FromBatch,
    string ToWarehouse,
    string ToWarehouseText,
    string ToStorageBin,
    string ToBatch,
    decimal QuantityKg,
    DateOnly? ProductionDate = null);

internal sealed record ExpectedTransferMovement(
    string Position,
    string BookingKey,
    string Article,
    string Batch,
    string Warehouse,
    string StorageBin,
    decimal QuantityKg);
