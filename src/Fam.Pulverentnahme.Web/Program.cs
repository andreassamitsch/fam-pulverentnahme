using Fam.Pulverentnahme.Web;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<OxaionOptions>(builder.Configuration.GetSection("Oxaion"));
builder.Services.Configure<SyncosOptions>(builder.Configuration.GetSection("Syncos"));
builder.Services.Configure<PrototypeOptions>(builder.Configuration.GetSection("Prototype"));
builder.Services.Configure<MachineTankOptions>(builder.Configuration.GetSection("MachineTanks"));
builder.Services.AddHttpClient(nameof(OxaionClient));
builder.Services.AddSingleton<JsonTransactionStore>();
builder.Services.AddSingleton<RejectedScanEventStore>();
builder.Services.AddSingleton<OxaionClient>();
builder.Services.AddSingleton<MachineStockService>();
builder.Services.AddSingleton<MachineTankService>();
builder.Services.AddSingleton<SourceStockService>();
builder.Services.AddSingleton<PersonnelService>();
builder.Services.AddSingleton<RfidPersonnelService>();
builder.Services.AddPersonnelAuthentication(builder.Configuration);
builder.Services.AddSingleton<MixBookingService>();
builder.Services.AddSeparateProcessFeatures();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UsePersonnelAuthentication();

app.MapGet("/api/health", (
    IOptions<OxaionOptions> options,
    IOptions<SyncosOptions> syncos,
    PersonnelAuthenticationService auth) => Results.Ok(new
{
    ok = true,
    environment = options.Value.StagingOnly ? "STAGING" : "UNRESTRICTED",
    serverUrl = options.Value.ServerUrl,
    firm = options.Value.Firm,
    user = options.Value.User,
    passwordConfigured = !string.IsNullOrWhiteSpace(options.Value.Password),
    syncosConfigured = !string.IsNullOrWhiteSpace(syncos.Value.ConnectionString),
    personnelAuthenticationConfigured = auth.IsConfigured
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
        var input = new Dictionary<string, string>(load.Dta, StringComparer.Ordinal)
        {
            ["KEYTYPE"] = "C_LKOPF"
        };
        await session.CallAsync("LB20100J", "*NEW", input, ct);
        return Results.Ok(new
        {
            ok = true,
            message = "Oxaion connect + LB20100J smoke test successful. No booking persisted."
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/api/machines", async (MachineTankService service, CancellationToken ct) =>
{
    try { return Results.Ok(await service.ReadOptionsAsync(ct)); }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/api/machines/{warehouse}/stock", async (
    string warehouse,
    MachineTankService service,
    CancellationToken ct) =>
{
    try { return Results.Ok(await service.ReadStockAsync(warehouse, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

// Diagnostics/backward compatibility. The worker flow uses /api/machines/{warehouse}/stock.
app.MapGet("/api/machine-stock", async (
    string warehouse,
    string article,
    string? warehouseText,
    string? articleText,
    MachineStockService service,
    CancellationToken ct) =>
{
    try { return Results.Ok(await service.ReadAsync(warehouse, article, warehouseText, articleText, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable); }
});

app.MapGet("/api/personnel/search", async (string q, PersonnelService service, CancellationToken ct) =>
{
    try { return Results.Ok(await service.SearchAsync(q, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPersonnelAuthentication();
app.MapSeparateProcessEndpoints();

// Preferred worker login: the RFID assignment is read from Syncos and the resulting personnel
// identity is revalidated exactly in Oxaion. Only after both checks succeed is the same backend
// personnel session set that is used by the password fallback.
app.MapPost("/api/personnel/nfc", async (
    NfcPersonnelRequest request,
    RfidPersonnelService service,
    HttpContext http,
    CancellationToken ct) =>
{
    try
    {
        var result = await service.ResolveAsync(request.SerialNumber, ct);
        if (result is null)
        {
            return Results.Json(new
            {
                status = "RFID_NOT_FOUND",
                message = "Der gelesene NFC-Chip ist in Syncos keiner aktiven sichtbaren Person zugeordnet."
            }, statusCode: StatusCodes.Status404NotFound);
        }

        http.Session.SetString(PersonnelAuthenticationSession.PersonnelNo, result.PersonnelNo);
        http.Session.SetString(PersonnelAuthenticationSession.PersonnelName, result.FullName);
        return Results.Ok(result);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { status = "RFID_INVALID", message = ex.Message });
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        return Results.Json(new
        {
            status = "RFID_LOOKUP_UNAVAILABLE",
            message = "NFC-Chip wurde gelesen, die Mitarbeiterzuordnung konnte aber nicht sicher bestätigt werden.",
            technicalMessage = ex.Message
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/api/source-stock/warehouses", async (
    string article,
    string? excludeWarehouse,
    SourceStockService service,
    CancellationToken ct) =>
{
    try
    {
        var rows = await service.ReadWarehousesAsync(article, ct);
        if (!string.IsNullOrWhiteSpace(excludeWarehouse))
            rows = rows.Where(x => !string.Equals(
                    x.Warehouse,
                    excludeWarehouse.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        return Results.Ok(rows);
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable); }
});

app.MapGet("/api/source-stock/positions", async (
    string article,
    string warehouse,
    SourceStockService service,
    CancellationToken ct) =>
{
    try { return Results.Ok(await service.ReadPositionsAsync(article, warehouse, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable); }
});

// Rejected physical charge scans are pre-booking audit events. They are intentionally separate
// from Oxaion transaction status such as REJECTED and contain no password, RFID or raw QR payload.
app.MapPost("/api/scan-events/rejected-charge", async (
    RejectedChargeScanRequest request,
    HttpContext http,
    RejectedScanEventStore store,
    CancellationToken ct) =>
{
    var personnelNo = http.Session.GetString(PersonnelAuthenticationSession.PersonnelNo);
    var personnelName = http.Session.GetString(PersonnelAuthenticationSession.PersonnelName);
    if (string.IsNullOrWhiteSpace(personnelNo) || string.IsNullOrWhiteSpace(personnelName))
    {
        return Results.Json(new
        {
            status = "AUTH_REQUIRED",
            message = "Fehlscan konnte keiner angemeldeten Person zugeordnet werden."
        }, statusCode: StatusCodes.Status401Unauthorized);
    }

    try
    {
        var evt = await store.SaveAsync(request, personnelNo, personnelName, ct);
        return Results.Ok(new { recorded = true, eventId = evt.EventId, recordedAtUtc = evt.RecordedAtUtc });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { recorded = false, error = ex.Message });
    }
});

app.MapPost("/api/mix", async (
    RealMixRequest request,
    MixBookingService service,
    MachineTankService machineTanks,
    SourceStockService sourceStock,
    PersonnelService personnel,
    CancellationToken ct) =>
{
    try
    {
        // Idempotency has priority inside an authenticated request.
        var existing = await service.GetAsync(request.ClientOperationId, ct);
        if (existing is not null) return TransactionResult(existing);

        var isRejectedRetry = !string.IsNullOrWhiteSpace(request.RetryOfClientOperationId);
        if (isRejectedRetry)
        {
            if (string.Equals(request.RetryOfClientOperationId, request.ClientOperationId, StringComparison.Ordinal))
                return Results.BadRequest(new { error = "A retry must use a new clientOperationId." });

            var rejected = await service.GetAsync(request.RetryOfClientOperationId!, ct);
            if (rejected is null)
                return Results.BadRequest(new { error = "The referenced rejected operation was not found." });
            if (!IsConfirmedRejected(rejected))
                return Results.BadRequest(new { error = "Only a confirmed REJECTED operation may be retried as a new operation." });
            if (!MixRequestLogic.SameBookingData(rejected.Request, request))
                return Results.BadRequest(new
                {
                    error = "A retry of a rejected operation must use the same booking data, including all replenishment batches. Start a normal new operation if booking data must change."
                });
        }
        else
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            if (request.BookingDate != today)
                return Results.BadRequest(new { error = "Buchungsdatum muss beim Nachfüllen dem aktuellen Datum entsprechen." });
            if (request.ProductionDate != today)
                return Results.BadRequest(new { error = "Mix Charge erstellt am muss beim neuen Nachfüllvorgang dem aktuellen Datum entsprechen." });
            if (!string.Equals(request.TargetWarehouse, request.OldMixWarehouse, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "Ziel der neuen Mix-Charge muss die ausgewählte Maschine sein." });
            if (!string.Equals(request.BookingText, ReplenishmentRules.BookingText(request.OldMixWarehouse), StringComparison.Ordinal))
                return Results.BadRequest(new { error = "Buchungstext stimmt nicht mit der ausgewählten Maschine überein." });
            if (!ReplenishmentRules.IsValidGeneratedMixBatch(request.Article, request.TargetBatch, request.ProductionDate))
                return Results.BadRequest(new
                {
                    error = "Neue Mix-Charge entspricht nicht dem festgelegten Schema Artikel-ohne-Punkt + MIX_ + yyyyMMdd_HHmmss."
                });
        }

        if (!machineTanks.IsAllowed(request.OldMixWarehouse))
            return Results.BadRequest(new { error = "Ausgewählter Maschinen-Lagerort ist nicht in der Maschinenliste freigegeben." });
        if (!string.Equals(request.TargetWarehouse, request.OldMixWarehouse, StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { error = "Ziel-Lagerort muss der ausgewählte Maschinen-Lagerort sein." });
        if (MixRequestLogic.Sources(request).Any(s => ReplenishmentRules.IsMachineSource(request, s)))
            return Results.BadRequest(new { error = "Der Maschinen-Tanklagerort darf nicht als Nachfüllquelle verwendet werden." });

        // Personnel remains Oxaion master data. Re-read immediately before write-capable calls.
        PersonnelOption? employee;
        try { employee = await personnel.ReadExactAsync(request.PersonnelNo, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Results.Json(new
            {
                status = "PERSONNEL_UNAVAILABLE",
                stage = "PERSONNEL_VALIDATION",
                message = "Mitarbeiter konnte vor der Buchung nicht sicher in oxaion geprüft werden. Es wurde keine Materialbuchung gestartet.",
                technicalMessage = ex.Message
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (employee is null || !string.Equals(employee.FullName, request.PersonnelName, StringComparison.Ordinal))
        {
            return Results.Json(new
            {
                status = "CONFLICT",
                stage = "PERSONNEL_VALIDATION",
                message = "Der ausgewählte Mitarbeiter ist in oxaion nicht mehr eindeutig bestätigt. Es wurde keine Materialbuchung gestartet."
            }, statusCode: StatusCodes.Status409Conflict);
        }

        // Safety gate 1: current tank stock.
        MachineStockResult stock;
        try { stock = await machineTanks.ReadStockAsync(request.OldMixWarehouse, ct); }
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
                message = $"Der Maschinenbestand hat sich seit der Anzeige geändert. Aktuell: {current.Article}, Charge {current.Batch}, {current.QuantityKg:0.###} kg. Angefordert: {request.Article}, Charge {request.OldMixBatch}, {request.OldMixAmountKg:0.###} kg. Es wurde keine Materialbuchung gestartet.",
                machineStock = stock
            }, statusCode: StatusCodes.Status409Conflict);
        }

        if (!string.IsNullOrWhiteSpace(current.ArticleText) &&
            !string.Equals(current.ArticleText, request.ArticleText, StringComparison.Ordinal))
        {
            return Results.BadRequest(new
            {
                error = "Artikelbezeichnung stimmt nicht mit dem aus oxaion gelesenen Maschinenbestand überein."
            });
        }

        // Safety gate 2: exact source positions and available quantities.
        SourceStockValidationResult sourceValidation;
        try
        {
            sourceValidation = await sourceStock.ValidateSourcesAsync(
                request.Article,
                MixRequestLogic.Sources(request),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Results.Json(new
            {
                status = "SOURCE_STOCK_UNAVAILABLE",
                stage = "SOURCE_STOCK_VALIDATION",
                message = "Die Nachfüllbestände konnten vor der Buchung nicht sicher aus oxaion gelesen werden. Es wurde keine Materialbuchung gestartet.",
                technicalMessage = ex.Message
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!sourceValidation.IsValid)
        {
            return Results.Json(new
            {
                status = "CONFLICT",
                stage = "SOURCE_STOCK_VALIDATION",
                message = sourceValidation.Message + " Es wurde keine Materialbuchung gestartet.",
                sourceStock = sourceValidation.CurrentPositions
            }, statusCode: StatusCodes.Status409Conflict);
        }

        var tx = await service.ExecuteAsync(request, ct);
        return TransactionResult(tx);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).AddEndpointFilter<PersonnelBookingAuthorizationFilter>();

app.MapGet("/api/mix/{clientOperationId}", async (
    string clientOperationId,
    MixBookingService service,
    CancellationToken ct) =>
{
    var tx = await service.GetAsync(clientOperationId, ct);
    if (tx is null) return Results.NotFound();
    if (IsConfirmedRejected(tx)) tx.Status = TransactionStatuses.Rejected;
    return Results.Ok(tx.ToResponse());
});

app.MapPost("/api/mix/{clientOperationId}/reconcile", async (
    string clientOperationId,
    MixBookingService service,
    CancellationToken ct) =>
{
    var existing = await service.GetAsync(clientOperationId, ct);
    if (existing is null) return Results.NotFound();
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
    TransactionStatuses.Uncertain or TransactionStatuses.ManualReviewRequired =>
        Results.Json(tx.ToResponse(), statusCode: StatusCodes.Status409Conflict),
    _ => Results.Accepted($"/api/mix/{tx.ClientOperationId}", tx.ToResponse())
};

static bool StockMatchesRequest(MachineStockRow stock, RealMixRequest request) =>
    string.Equals(stock.Warehouse, request.OldMixWarehouse, StringComparison.OrdinalIgnoreCase) &&
    string.Equals(stock.Article, request.Article, StringComparison.OrdinalIgnoreCase) &&
    string.Equals(stock.Batch, request.OldMixBatch, StringComparison.Ordinal) &&
    Math.Abs(stock.QuantityKg - request.OldMixAmountKg) < 0.0005m;

static bool IsConfirmedRejected(MixTransaction tx) =>
    tx.Status == TransactionStatuses.Rejected ||
    (string.IsNullOrWhiteSpace(tx.DocumentNo) && tx.Events.Any(e => e.Stage == "REJECTED"));

app.MapFallbackToFile("index.html");
app.Run();

public sealed record NfcPersonnelRequest(string SerialNumber);