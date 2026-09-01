using Fam.Pulverentnahme.Web;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<OxaionOptions>(builder.Configuration.GetSection("Oxaion"));
builder.Services.Configure<PrototypeOptions>(builder.Configuration.GetSection("Prototype"));
builder.Services.AddHttpClient(nameof(OxaionClient));
builder.Services.AddSingleton<JsonTransactionStore>();
builder.Services.AddSingleton<OxaionClient>();
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

app.MapPost("/api/mix", async (RealMixRequest request, MixBookingService service, CancellationToken ct) =>
{
    try
    {
        if (!string.IsNullOrWhiteSpace(request.RetryOfClientOperationId))
        {
            if (string.Equals(request.RetryOfClientOperationId, request.ClientOperationId, StringComparison.Ordinal))
                return Results.BadRequest(new { error = "A retry must use a new clientOperationId." });

            var rejected = await service.GetAsync(request.RetryOfClientOperationId, ct);
            if (rejected is null)
                return Results.BadRequest(new { error = "The referenced rejected operation was not found." });
            if (rejected.Status != TransactionStatuses.Rejected)
                return Results.BadRequest(new { error = "Only a confirmed REJECTED operation may be retried as a new operation." });
            if (!SameBookingData(rejected.Request, request))
                return Results.BadRequest(new { error = "A retry of a rejected operation must use the same booking data. Start a normal new operation if booking data must change." });
        }

        var tx = await service.ExecuteAsync(request, ct);
        return tx.Status switch
        {
            TransactionStatuses.Success => Results.Ok(tx.ToResponse()),
            TransactionStatuses.Rejected => Results.Json(tx.ToResponse(), statusCode: StatusCodes.Status422UnprocessableEntity),
            TransactionStatuses.Uncertain or TransactionStatuses.ManualReviewRequired => Results.Json(tx.ToResponse(), statusCode: StatusCodes.Status409Conflict),
            _ => Results.Accepted($"/api/mix/{tx.ClientOperationId}", tx.ToResponse())
        };
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/mix/{clientOperationId}", async (string clientOperationId, MixBookingService service, CancellationToken ct) =>
{
    var tx = await service.GetAsync(clientOperationId, ct);
    return tx is null ? Results.NotFound() : Results.Ok(tx.ToResponse());
});

app.MapPost("/api/mix/{clientOperationId}/reconcile", async (string clientOperationId, MixBookingService service, CancellationToken ct) =>
{
    var existing = await service.GetAsync(clientOperationId, ct);
    if (existing is null) return Results.NotFound();

    // A confirmed Oxaion rejection is terminal for this clientOperationId.
    // Reconcile is only for uncertain/partial outcomes; it must never turn a
    // clear REJECTED result into MANUAL_REVIEW_REQUIRED merely because no
    // document number exists.
    if (existing.Status == TransactionStatuses.Rejected)
        return Results.Json(existing.ToResponse(), statusCode: StatusCodes.Status422UnprocessableEntity);

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

static bool SameBookingData(RealMixRequest source, RealMixRequest retry)
{
    var a = source with { ClientOperationId = "", SimulateFailure = null, RetryOfClientOperationId = null };
    var b = retry with { ClientOperationId = "", SimulateFailure = null, RetryOfClientOperationId = null };
    return a == b;
}

app.MapFallbackToFile("index.html");
app.Run();
