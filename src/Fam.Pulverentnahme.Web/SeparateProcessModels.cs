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
    string TargetStorageBin) : ISeparatePersonnelRequest;

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
    decimal? TargetFaConsumedKg = null);

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
        tx.TargetFaConsumedKg);
}

public sealed class SeparateOperationStore
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _directory;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public SeparateOperationStore(IWebHostEnvironment env)
    {
        _directory = Path.Combine(env.ContentRootPath, "App_Data", "process-transactions");
        Directory.CreateDirectory(_directory);
    }

    public SemaphoreSlim GetLock(string kind, string id) =>
        _locks.GetOrAdd(kind + ":" + id, _ => new SemaphoreSlim(1, 1));

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

    public static string SerializeRequest<T>(T request) => JsonSerializer.Serialize(request, JsonOptions);

    private string PathFor(string kind, string id)
    {
        static string Safe(string value) => string.Concat((value ?? "").Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
        var k = Safe(kind);
        var i = Safe(id);
        if (k.Length == 0 || i.Length == 0) throw new ArgumentException("Invalid transaction key.");
        return Path.Combine(_directory, k + "_" + i + ".json");
    }
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
