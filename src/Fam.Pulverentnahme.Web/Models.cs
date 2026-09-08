namespace Fam.Pulverentnahme.Web;

public sealed class OxaionOptions
{
    public string ServerUrl { get; set; } = "";
    public string User { get; set; } = "";
    public string Firm { get; set; } = "";
    public string Password { get; set; } = "";
    public bool StagingOnly { get; set; } = true;
}

public sealed class SyncosOptions
{
    public string ConnectionString { get; set; } = "";
}

public sealed class OxaionSqlOptions
{
    public string ConnectionString { get; set; } = "";
}

public sealed class PrototypeOptions
{
    public bool EnableFailureSimulation { get; set; } = true;
    public string TransactionDirectory { get; set; } = "App_Data/transactions";
}

public sealed class MachineTankOptions
{
    public List<string> Warehouses { get; set; } = [];
}

public sealed record AdditionalPowderSource(
    string Warehouse,
    string WarehouseText,
    string StorageBin,
    string Batch,
    decimal AmountKg);

public sealed record RealMixRequest(
    string ClientOperationId,
    string PersonnelNo,
    string PersonnelName,
    string Article,
    string ArticleText,
    string OldMixWarehouse,
    string OldMixWarehouseText,
    string OldMixStorageBin,
    string OldMixBatch,
    decimal OldMixAmountKg,
    // Legacy single-source fields are intentionally retained for stored STAGING transactions
    // and older clients. New clients additionally send AdditionalSources and mirror its first
    // entry into these fields. Remove only with an explicit migration of persisted transactions.
    string AddWarehouse,
    string AddWarehouseText,
    string AddStorageBin,
    string AddBatch,
    decimal AddAmountKg,
    string TargetWarehouse,
    string TargetWarehouseText,
    string TargetStorageBin,
    string TargetBatch,
    DateOnly ProductionDate,
    DateOnly BookingDate,
    string BookingText,
    string? SimulateFailure = null,
    string? RetryOfClientOperationId = null,
    IReadOnlyList<AdditionalPowderSource>? AdditionalSources = null);

public static class ReplenishmentRules
{
    public static string BookingText(string machineWarehouse) => $"Pulver nachfüllen {(machineWarehouse ?? "").Trim()}";

    public static string BatchArticlePrefix(string article) =>
        new((article ?? "").Where(char.IsLetterOrDigit).ToArray());

    public static string CreateMixBatch(string article, DateTime createdAt) =>
        $"{BatchArticlePrefix(article)}MIX_{createdAt:yyyyMMdd_HHmmss}";

    public static bool IsValidGeneratedMixBatch(string article, string batch, DateOnly createdOn)
    {
        var prefix = BatchArticlePrefix(article) + "MIX_" + createdOn.ToString("yyyyMMdd") + "_";
        if (string.IsNullOrWhiteSpace(batch) || !batch.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var time = batch[prefix.Length..];
        if (time.Length != 6 || !time.All(char.IsDigit)) return false;
        return int.TryParse(time[..2], out var h) && h is >= 0 and <= 23
            && int.TryParse(time.Substring(2, 2), out var m) && m is >= 0 and <= 59
            && int.TryParse(time.Substring(4, 2), out var s) && s is >= 0 and <= 59;
    }

    public static bool IsMachineSource(RealMixRequest request, AdditionalPowderSource source) =>
        string.Equals(request.OldMixWarehouse, source.Warehouse, StringComparison.OrdinalIgnoreCase);
}

public static class MixRequestLogic
{
    public static IReadOnlyList<AdditionalPowderSource> Sources(RealMixRequest request)
    {
        if (request.AdditionalSources is { Count: > 0 })
            return request.AdditionalSources;

        return
        [
            new AdditionalPowderSource(
                request.AddWarehouse,
                request.AddWarehouseText,
                request.AddStorageBin,
                request.AddBatch,
                request.AddAmountKg)
        ];
    }

    public static bool LegacyFirstSourceMatches(RealMixRequest request)
    {
        if (request.AdditionalSources is not { Count: > 0 }) return true;
        var first = request.AdditionalSources[0];
        return string.Equals(request.AddWarehouse, first.Warehouse, StringComparison.Ordinal)
            && string.Equals(request.AddWarehouseText, first.WarehouseText, StringComparison.Ordinal)
            && string.Equals(request.AddStorageBin, first.StorageBin, StringComparison.Ordinal)
            && string.Equals(request.AddBatch, first.Batch, StringComparison.Ordinal)
            && request.AddAmountKg == first.AmountKg;
    }

    public static bool SameBookingData(RealMixRequest a, RealMixRequest b)
    {
        if (!string.Equals(a.PersonnelNo, b.PersonnelNo, StringComparison.Ordinal)
            || !string.Equals(a.PersonnelName, b.PersonnelName, StringComparison.Ordinal)
            || !string.Equals(a.Article, b.Article, StringComparison.Ordinal)
            || !string.Equals(a.ArticleText, b.ArticleText, StringComparison.Ordinal)
            || !string.Equals(a.OldMixWarehouse, b.OldMixWarehouse, StringComparison.Ordinal)
            || !string.Equals(a.OldMixWarehouseText, b.OldMixWarehouseText, StringComparison.Ordinal)
            || !string.Equals(a.OldMixStorageBin, b.OldMixStorageBin, StringComparison.Ordinal)
            || !string.Equals(a.OldMixBatch, b.OldMixBatch, StringComparison.Ordinal)
            || a.OldMixAmountKg != b.OldMixAmountKg
            || !string.Equals(a.TargetWarehouse, b.TargetWarehouse, StringComparison.Ordinal)
            || !string.Equals(a.TargetWarehouseText, b.TargetWarehouseText, StringComparison.Ordinal)
            || !string.Equals(a.TargetStorageBin, b.TargetStorageBin, StringComparison.Ordinal)
            || !string.Equals(a.TargetBatch, b.TargetBatch, StringComparison.Ordinal)
            || a.ProductionDate != b.ProductionDate
            || a.BookingDate != b.BookingDate
            || !string.Equals(a.BookingText, b.BookingText, StringComparison.Ordinal))
            return false;

        var left = Sources(a);
        var right = Sources(b);
        return left.Count == right.Count && left.Zip(right).All(pair => pair.First == pair.Second);
    }
}

public static class TransactionStatuses
{
    public const string Created = "CREATED";
    public const string Validating = "VALIDATING";
    public const string SendingToOxaion = "SENDING_TO_OXAION";
    public const string Position1Confirmed = "POSITION_1_CONFIRMED";
    public const string Position2Confirmed = "POSITION_2_CONFIRMED";
    public const string Success = "SUCCESS";
    public const string Rejected = "REJECTED";
    public const string Conflict = "CONFLICT";
    public const string Uncertain = "UNCERTAIN";
    public const string ManualReviewRequired = "MANUAL_REVIEW_REQUIRED";
}

public sealed class MixTransaction
{
    public string ClientOperationId { get; set; } = "";
    public string TransactionId { get; set; } = Guid.NewGuid().ToString("N");
    public string Status { get; set; } = TransactionStatuses.Created;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Stage { get; set; } = "CREATED";
    public string Message { get; set; } = "";
    public string? DocumentNo { get; set; }
    public RealMixRequest Request { get; set; } = default!;
    public Dictionary<string, string>? HeaderDta { get; set; }
    public Dictionary<string, string>? Position1ValidatedState { get; set; }
    public Dictionary<string, Dictionary<string, string>> PositionValidatedStates { get; set; } = new(StringComparer.Ordinal);
    public List<MovementRow> LastMovements { get; set; } = [];
    public List<TransactionEvent> Events { get; set; } = [];
}

public sealed record TransactionEvent(DateTimeOffset At, string Stage, string Message);

public sealed record MovementRow(
    string Position,
    string BookingKey,
    string Article,
    string Batch,
    string Warehouse,
    string StorageBin,
    decimal Quantity,
    string Timestamp);

public sealed record ReconcileResult(
    string Status,
    string Action,
    string Message,
    IReadOnlyList<MovementRow> Rows,
    int CompletedPositions = 0);

public sealed record ApiTransactionResponse(
    string ClientOperationId,
    string TransactionId,
    string? RetryOfClientOperationId,
    string Status,
    string Stage,
    string Message,
    string? DocumentNo,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<MovementRow> Movements);

public static class TransactionMapping
{
    public static ApiTransactionResponse ToResponse(this MixTransaction tx) => new(
        tx.ClientOperationId,
        tx.TransactionId,
        tx.Request.RetryOfClientOperationId,
        tx.Status,
        tx.Stage,
        tx.Message,
        tx.DocumentNo,
        tx.UpdatedAt,
        tx.LastMovements);
}
