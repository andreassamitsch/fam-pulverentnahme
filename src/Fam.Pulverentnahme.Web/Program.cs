using Fam.Pulverentnahme.Web;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<OxaionOptions>(builder.Configuration.GetSection("Oxaion"));
builder.Services.Configure<PrototypeOptions>(builder.Configuration.GetSection("Prototype"));
builder.Services.AddHttpClient(nameof(OxaionClient));
builder.Services.AddSingleton<JsonTransactionStore>();
builder.Services.AddSingleton<OxaionClient>();
builder.Services.AddSingleton<MachineStockService>();
builder.Services.AddSingleton<MixBookingService>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", (IOptions<OxaionOptions> options) => Results.Ok(new
{
    ok = true,
    environment = options.Value.StagingOnly ? "STAGING" : "UNRESTRICTED",
    serverUrl = options.Value.ServerUrl,
    firm = options.Value.Firm,
    user = options.Value.User,
    passwordConfigured = !string.IsNullOrWhiteSpace(options.Value.Password)
}));

app.MapGet("/api/health/oxaion", async (OxaionClient oxaion, CancellationToken ct) =>
{
    try
    {
        await using var session = await oxaion.ConnectAsync(ct);
        var load = await session.CallAsync("LB20100J", "*LOADNEW", new Dictionary<string, string>
        {
            ["ISSID"] = "HTTPWEB" + Guid.NewGuid().ToString("N"),
            ["NOHWPgm"] = "LB20100",
            ["SSID"] = "",
            ["KOBGNR"] = "",
            ["KEYTYPE"] = "C_LKOPF"
        }, ct);
        var input = new Dictionary<string, string>(load.Dta, StringComparer.Ordinal) { ["KEYTYPE"] = "C_LKOPF" };
        await session.CallAsync("LB20100J", "*NEW", input, ct);
        return Results.Ok(new { ok = true, message = "Oxaion connect + LB20100J smoke test successful. No booking persisted." });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/api/machine-stock", async (
    string warehouse,
    string article,
    string? warehouseText,
    string? articleText,
    MachineStockService service,
    CancellationToken ct) =>
{
    try
    {
        var stock = await service.ReadAsync(warehouse, article, warehouseText, articleText, ct);
        return Results.Ok(stock);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/api/mix", async (
    RealMixRequest request,
    MixBookingService service,
    MachineStockService machineStock,
    CancellationToken ct) =>
{
    try
    {
        // Idempotency has priority over a fresh stock check. If the same clientOperationId
        // already exists, return the stored transaction instead of interpreting today's
        // machine state as a reason to create or reject a second ERP operation.
        var existing = await service.GetAsync(request.ClientOperationId, ct);
        if (existing is not null)
            return TransactionResult(existing);

        if (!string.IsNullOrWhiteSpace(request.RetryOfClientOperationId))
        {
            if (string.Equals(request.RetryOfClientOperationId, request.ClientOperationId, StringComparison.Ordinal))
                return Results.BadRequest(new { error = "A retry must use a new clientOperationId." });

            var rejected = await service.GetAsync(request.RetryOfClientOperationId, ct);
            if (rejected is null)
                return Results.BadRequest(new { error = "The referenced rejected operation was not found." });
            if (!IsConfirmedRejected(rejected))
                return Results.BadRequest(new { error = "Only a confirmed REJECTED operation may be retried as a new operation." });
            if (!SameBookingData(rejected.Request, request))
                return Results.BadRequest(new { error = "A retry of a rejected operation must use the same booking data. Start a normal new operation if booking data must change." });
        }

        // Safety gate before any write-capable Oxaion material-booking call.
        // Re-read the positive machine stock and require the request to still match exactly.
        MachineStockResult stock;
        try
        {
            stock = await machineStock.ReadAsync(
                request.OldMixWarehouse,
                request.Article,
                request.OldMixWarehouseText,
                request.ArticleText,
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Results.Json(new
            {
                status = "MACHINE_STOCK_UNAVAILABLE",
                stage = "MACHINE_STOCK_VALIDATION",
                message = "Aktueller Maschinenbestand konnte vor der Buchung nicht sicher aus oxaion gelesen werden. Es wurde keine Materialbuchung gestartet.",
                technicalMessage = ex.Message
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (stock.Status != MachineStockStatuses.Unique || stock.Rows.Count != 1)
        {
            return Results.Json(new
            {
                status = "CONFLICT",
                stage = "MACHINE_STOCK_VALIDATION",
                message = stock.Message + " Es wurde keine Materialbuchung gestartet.",
                machineStock = stock
            }, statusCode: StatusCodes.Status409Conflict);
        }

        var current = stock.Rows[0];
        if (!StockMatchesRequest(current, request))
        {
            return Results.Json(new
            {
                status = "CONFLICT",
                stage = "MACHINE_STOCK_VALIDATION",
                message = $"Der Maschinenbestand hat sich seit der Anzeige geändert. Aktuell: Charge {current.Batch}, {current.QuantityKg:0.###} kg. Angefordert: Charge {request.OldMixBatch}, {request.OldMixAmountKg:0.###} kg. Es wurde keine Materialbuchung gestartet.",
                machineStock = stock
            }, statusCode: StatusCodes.Status409Conflict);
        }

        var tx = await service.ExecuteAsync(request, ct);
        return TransactionResult(tx);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/mix/{clientOperationId}", async (string clientOperationId, MixBookingService service, CancellationToken ct) =>
{
    var tx = await service.GetAsync(clientOperationId, ct);
    if (tx is null) return Results.NotFound();
    if (IsConfirmedRejected(tx)) tx.Status = TransactionStatuses.Rejected;
    return Results.Ok(tx.ToResponse());
});

app.MapPost("/api/mix/{clientOperationId}/reconcile", async (string clientOperationId, MixBookingService service, CancellationToken ct) =>
{
    var existing = await service.GetAsync(clientOperationId, ct);
    if (existing is null) return Results.NotFound();

    // A confirmed Oxaion rejection is terminal for this clientOperationId.
    // Older prototype versions could subsequently overwrite the stored status
    // with MANUAL_REVIEW_REQUIRED. The historical REJECTED event plus the
    // absence of a document number is sufficient to recognize that known case.
    if (IsConfirmedRejected(existing))
    {
        existing.Status = TransactionStatuses.Rejected;
        return Results.Json(existing.ToResponse(), statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    try
    {
        var tx = await service.ReconcileAsync(clientOperationId, ct);
        return tx.Status switch
        {
            TransactionStatuses.Success => Results.Ok(tx.ToResponse()),
            TransactionStatuses.Rejected => Results.Json(tx.ToResponse(), statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => Results.Json(tx.ToResponse(), statusCode: StatusCodes.Status409Conflict)
        };
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
});

static IResult TransactionResult(MixTransaction tx) => tx.Status switch
{
    TransactionStatuses.Success => Results.Ok(tx.ToResponse()),
    TransactionStatuses.Rejected => Results.Json(tx.ToResponse(), statusCode: StatusCodes.Status422UnprocessableEntity),
    TransactionStatuses.Uncertain or TransactionStatuses.ManualReviewRequired => Results.Json(tx.ToResponse(), statusCode: StatusCodes.Status409Conflict),
    _ => Results.Accepted($"/api/mix/{tx.ClientOperationId}", tx.ToResponse())
};

static bool StockMatchesRequest(MachineStockRow stock, RealMixRequest request) =>
    string.Equals(stock.Warehouse, request.OldMixWarehouse, StringComparison.OrdinalIgnoreCase) &&
    string.Equals(stock.Article, request.Article, StringComparison.OrdinalIgnoreCase) &&
    string.Equals(stock.Batch, request.OldMixBatch, StringComparison.Ordinal) &&
    Math.Abs(stock.QuantityKg - request.OldMixAmountKg) < 0.0005m;

static bool SameBookingData(RealMixRequest source, RealMixRequest retry)
{
    var a = source with { ClientOperationId = "", SimulateFailure = null, RetryOfClientOperationId = null };
    var b = retry with { ClientOperationId = "", SimulateFailure = null, RetryOfClientOperationId = null };
    return a == b;
}

static bool IsConfirmedRejected(MixTransaction tx) =>
    tx.Status == TransactionStatuses.Rejected ||
    (string.IsNullOrWhiteSpace(tx.DocumentNo) && tx.Events.Any(e => e.Stage == "REJECTED"));

app.MapFallbackToFile("index.html");
app.Run();
