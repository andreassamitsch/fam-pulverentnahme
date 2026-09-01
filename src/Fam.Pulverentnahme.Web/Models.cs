using System.Text.Json.Serialization;

namespace Fam.Pulverentnahme.Web;

public sealed class OxaionOptions
{
    public string ServerUrl { get; set; } = "";
    public string User { get; set; } = "";
    public string Firm { get; set; } = "";
    public string Password { get; set; } = "";
    public bool StagingOnly { get; set; } = true;
}

public sealed class PrototypeOptions
{
    public bool EnableFailureSimulation { get; set; } = true;
    public string TransactionDirectory { get; set; } = "App_Data/transactions";
}

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
    string? RetryOfClientOperationId = null);

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

public sealed record ReconcileResult(string Status, string Action, string Message, IReadOnlyList<MovementRow> Rows);

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
