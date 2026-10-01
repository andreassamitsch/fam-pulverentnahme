using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace Fam.Pulverentnahme.Web;

public static class SeparateProcessFeatureExtensions
{
    public static IServiceCollection AddSeparateProcessFeatures(this IServiceCollection services)
    {
        services.AddSingleton<SeparateOperationStore>();
        services.AddSingleton<MaterialTransferBookingService>();
        services.AddSingleton<TankOutService>();
        services.AddSingleton<TankOutLabelPrintService>();
        services.AddSingleton<FillNewService>();
        services.AddSingleton<FaMaterialService>();
        services.AddSingleton<FaConsumptionService>();
        services.AddSingleton<FaAbortCorrectionService>();
        services.AddSingleton<InventoryCorrectionService>();
        services.AddSingleton<InventoryService>();
        services.AddSingleton<InventoryOverviewService>();
        services.AddSingleton<TargetLocationLookupService>();
        return services;
    }

    public static IEndpointRouteBuilder MapSeparateProcessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Lightweight reachability/authentication probe for the operator-visible status lamp.
        // It opens and closes only an Oxaion app-tunnel session; unlike /api/health/oxaion it does
        // not execute an LB20100 dialog smoke test and is therefore suitable for periodic polling.
        endpoints.MapGet("/api/connectivity/oxaion", async (OxaionClient oxaion, CancellationToken ct) =>
        {
            try
            {
                await using var session = await oxaion.ConnectAsync(ct);
                return Results.Ok(new { ok=true, message="Oxaion erreichbar." });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.Json(new { ok=false, message="Oxaion derzeit nicht erreichbar.", technicalMessage=ex.Message }, statusCode:503);
            }
        });

        endpoints.MapGet("/api/article-recognition-colors", async (string article, HttpContext http, OxaionClient oxaion, CancellationToken ct) =>
        {
            if (!SessionAuthenticated(http, out var auth)) return auth!;
            try
            {
                await using var session = await oxaion.ConnectAsync(ct);
                return Results.Ok(await ArticleRecognitionColorLookup.ReadAsync(session, article, ct));
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error=ex.Message }); }
            catch (Exception ex) when (ex is not OperationCanceledException) { return Results.Problem(ex.Message, statusCode:503); }
        });

        endpoints.MapGet("/api/fa-material", async (string orderNo, string article, HttpContext http, FaMaterialService service, CancellationToken ct) =>
        {
            if (!SessionAuthenticated(http, out var auth)) return auth!;
            try { return Results.Ok(await service.FindUniqueAsync(orderNo, article, ct)); }
            catch (ProcessConflictException ex) { return Results.Json(new { status="CONFLICT", message=ex.Message }, statusCode:409); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error=ex.Message }); }
            catch (Exception ex) when (ex is not OperationCanceledException) { return Results.Problem(ex.Message, statusCode:503); }
        });

        endpoints.MapGet("/api/inventory/rp-stock", async (HttpContext http, InventoryService service, CancellationToken ct) =>
        {
            if (!SessionAuthenticated(http, out var auth)) return auth!;
            try { return Results.Ok(await service.ReadRpStockAsync(ct)); }
            catch (Exception ex) when (ex is not OperationCanceledException) { return Results.Problem(ex.Message, statusCode:503); }
        });

        endpoints.MapGet("/api/inventory/overview", async (HttpContext http, InventoryOverviewService service, CancellationToken ct) =>
        {
            if (!SessionAuthenticated(http, out var auth)) return auth!;
            try { return Results.Ok(await service.ReadAsync(ct)); }
            catch (Exception ex) when (ex is not OperationCanceledException) { return Results.Problem(ex.Message, statusCode:503); }
        });

        endpoints.MapGet("/api/target-locations/warehouses", async (
            string? q,
            string? excludeWarehouse,
            HttpContext http,
            TargetLocationLookupService service,
            CancellationToken ct) =>
        {
            if (!SessionAuthenticated(http, out var auth)) return auth!;
            try { return Results.Ok(await service.SearchWarehousesAsync(q, excludeWarehouse, ct)); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error=ex.Message }); }
            catch (Exception ex) when (ex is not OperationCanceledException) { return Results.Problem(ex.Message, statusCode:503); }
        });

        endpoints.MapGet("/api/target-locations/storage-bins", async (
            string warehouse,
            string? q,
            HttpContext http,
            TargetLocationLookupService service,
            CancellationToken ct) =>
        {
            if (!SessionAuthenticated(http, out var auth)) return auth!;
            try { return Results.Ok(await service.SearchStorageBinsAsync(warehouse, q, ct)); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error=ex.Message }); }
            catch (Exception ex) when (ex is not OperationCanceledException) { return Results.Problem(ex.Message, statusCode:503); }
        });

        endpoints.MapPost("/api/tank-out", async (TankOutRequest request, HttpContext http, TankOutService service, CancellationToken ct) =>
        {
            if (!SessionMatches(http, request, out var auth)) return auth!;
            try { return OperationResult(await service.ExecuteAsync(request, ct)); }
            catch (ProcessConflictException ex) { return Results.Json(new { status="CONFLICT", message=ex.Message }, statusCode:409); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error=ex.Message }); }
        });
        endpoints.MapGet("/api/tank-out/label-reprint-candidates", async (
            string? q,
            int? limit,
            HttpContext http,
            TankOutLabelPrintService service,
            CancellationToken ct) =>
        {
            if (!SessionAuthenticated(http, out var auth)) return auth!;
            try { return Results.Ok(await service.ListReprintCandidatesAsync(q, limit ?? 30, ct)); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error=ex.Message }); }
            catch (Exception ex) when (ex is not OperationCanceledException) { return Results.Problem(ex.Message, statusCode:503); }
        });
        endpoints.MapGet("/api/tank-out/{id}", async (string id, HttpContext http, TankOutService service, CancellationToken ct) =>
        { if (!SessionAuthenticated(http, out var auth)) return auth!; return await service.GetAsync(id, ct) is { } tx ? Results.Ok(tx.ToResponse()) : Results.NotFound(); });
        endpoints.MapPost("/api/tank-out/{id}/reconcile", async (string id, HttpContext http, TankOutService service, CancellationToken ct) =>
        { if (!SessionAuthenticated(http, out var auth)) return auth!; try { return OperationResult(await service.ReconcileAsync(id, ct)); } catch (KeyNotFoundException) { return Results.NotFound(); } });

        endpoints.MapPost("/api/tank-out/{id}/labels", async (
            string id,
            TankOutLabelPrintRequest request,
            HttpContext http,
            TankOutLabelPrintService service,
            CancellationToken ct) =>
        {
            if (!SessionMatches(http, request, out var auth)) return auth!;
            if (!string.Equals(id, request.TankOutOperationId, StringComparison.Ordinal))
                return Results.BadRequest(new { error="Tank-out operation id in route and request must match." });
            try { return OperationResult(await service.ExecuteAsync(request, ct)); }
            catch (ProcessConflictException ex) { return Results.Json(new { status="CONFLICT", message=ex.Message }, statusCode:409); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error=ex.Message }); }
        });
        endpoints.MapGet("/api/tank-out-label-print/{id}", async (
            string id,
            HttpContext http,
            TankOutLabelPrintService service,
            CancellationToken ct) =>
        {
            if (!SessionAuthenticated(http, out var auth)) return auth!;
            return await service.GetAsync(id, ct) is { } tx ? Results.Ok(tx.ToResponse()) : Results.NotFound();
        });

        endpoints.MapPost("/api/fill-new", async (FillNewRequest request, HttpContext http, FillNewService service, CancellationToken ct) =>
        {
            if (!SessionMatches(http, request, out var auth)) return auth!;
            try { return OperationResult(await service.ExecuteAsync(request, ct)); }
            catch (ProcessConflictException ex) { return Results.Json(new { status="CONFLICT", message=ex.Message }, statusCode:409); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error=ex.Message }); }
        });
        endpoints.MapGet("/api/fill-new/{id}", async (string id, HttpContext http, FillNewService service, CancellationToken ct) =>
        { if (!SessionAuthenticated(http, out var auth)) return auth!; return await service.GetAsync(id, ct) is { } tx ? Results.Ok(tx.ToResponse()) : Results.NotFound(); });
        endpoints.MapPost("/api/fill-new/{id}/reconcile", async (string id, HttpContext http, FillNewService service, CancellationToken ct) =>
        { if (!SessionAuthenticated(http, out var auth)) return auth!; try { return OperationResult(await service.ReconcileAsync(id, ct)); } catch (KeyNotFoundException) { return Results.NotFound(); } });

        endpoints.MapPost("/api/fa-consumption", async (FaConsumptionRequest request, HttpContext http, FaConsumptionService service, CancellationToken ct) =>
        {
            if (!SessionMatches(http, request, out var auth)) return auth!;
            try { return OperationResult(await service.ExecuteAsync(request, ct)); }
            catch (ProcessConflictException ex) { return Results.Json(new { status="CONFLICT", message=ex.Message }, statusCode:409); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error=ex.Message }); }
        });
        endpoints.MapGet("/api/fa-consumption/{id}", async (string id, HttpContext http, FaConsumptionService service, CancellationToken ct) =>
        { if (!SessionAuthenticated(http, out var auth)) return auth!; return await service.GetAsync(id, ct) is { } tx ? Results.Ok(tx.ToResponse()) : Results.NotFound(); });
        endpoints.MapPost("/api/fa-consumption/{id}/reconcile", async (string id, HttpContext http, FaConsumptionService service, CancellationToken ct) =>
        { if (!SessionAuthenticated(http, out var auth)) return auth!; try { return OperationResult(await service.ReconcileAsync(id, ct)); } catch (KeyNotFoundException) { return Results.NotFound(); } });

        endpoints.MapPost("/api/fa-abort-correction/validate-source", async (FaAbortSourceCheckRequest request, HttpContext http, FaAbortCorrectionService service, CancellationToken ct) =>
        {
            if (!SessionMatches(http, request.PersonnelNo, request.PersonnelName, out var auth)) return auth!;
            try { return Results.Ok(await service.ValidateSourceAsync(request, ct)); }
            catch (ProcessConflictException ex) { return Results.Json(new { status="CONFLICT", message=ex.Message }, statusCode:409); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error=ex.Message }); }
            catch (Exception ex) when (ex is not OperationCanceledException) { return Results.Problem(ex.Message, statusCode:503); }
        });

        endpoints.MapPost("/api/fa-abort-correction", async (FaAbortCorrectionRequest request, HttpContext http, FaAbortCorrectionService service, CancellationToken ct) =>
        {
            if (!SessionMatches(http, request, out var auth)) return auth!;
            try { return OperationResult(await service.ExecuteAsync(request, ct)); }
            catch (ProcessConflictException ex) { return Results.Json(new { status="CONFLICT", message=ex.Message }, statusCode:409); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error=ex.Message }); }
        });
        endpoints.MapGet("/api/fa-abort-correction/{id}", async (string id, HttpContext http, FaAbortCorrectionService service, CancellationToken ct) =>
        { if (!SessionAuthenticated(http, out var auth)) return auth!; return await service.GetAsync(id, ct) is { } tx ? Results.Ok(tx.ToResponse()) : Results.NotFound(); });
        endpoints.MapPost("/api/fa-abort-correction/{id}/reconcile", async (string id, HttpContext http, FaAbortCorrectionService service, CancellationToken ct) =>
        { if (!SessionAuthenticated(http, out var auth)) return auth!; try { return OperationResult(await service.ReconcileAsync(id, ct)); } catch (KeyNotFoundException) { return Results.NotFound(); } });

        return endpoints;
    }

    private static bool SessionAuthenticated(HttpContext http, out IResult? result)
    {
        var no = http.Session.GetString(PersonnelAuthenticationSession.PersonnelNo);
        var name = http.Session.GetString(PersonnelAuthenticationSession.PersonnelName);
        if (string.IsNullOrWhiteSpace(no) || string.IsNullOrWhiteSpace(name))
        {
            result = Results.Json(new { status="AUTH_REQUIRED", message="Bitte Mitarbeiter anmelden." }, statusCode:401);
            return false;
        }
        result = null;
        return true;
    }

    private static bool SessionMatches(HttpContext http, string personnelNo, string personnelName, out IResult? result)
    {
        var no = http.Session.GetString(PersonnelAuthenticationSession.PersonnelNo);
        var name = http.Session.GetString(PersonnelAuthenticationSession.PersonnelName);
        if (string.IsNullOrWhiteSpace(no) || string.IsNullOrWhiteSpace(name))
        {
            result = Results.Json(new { status="AUTH_REQUIRED", stage="PERSONNEL_VALIDATION", message="Bitte Mitarbeiter anmelden." }, statusCode:401);
            return false;
        }
        if (!string.Equals(no, personnelNo, StringComparison.Ordinal) || !string.Equals(name, personnelName, StringComparison.Ordinal))
        {
            result = Results.Json(new { status="AUTH_CONFLICT", stage="PERSONNEL_VALIDATION", message="Angemeldeter Mitarbeiter stimmt nicht mit der Prüfung überein." }, statusCode:403);
            return false;
        }
        result = null;
        return true;
    }

    private static bool SessionMatches(HttpContext http, ISeparatePersonnelRequest request, out IResult? result)
    {
        var no = http.Session.GetString(PersonnelAuthenticationSession.PersonnelNo);
        var name = http.Session.GetString(PersonnelAuthenticationSession.PersonnelName);
        if (string.IsNullOrWhiteSpace(no) || string.IsNullOrWhiteSpace(name))
        {
            result = Results.Json(new { status="AUTH_REQUIRED", stage="PERSONNEL_VALIDATION", message="Bitte Mitarbeiter anmelden. Es wurde keine Materialbuchung gestartet." }, statusCode:401);
            return false;
        }
        if (!string.Equals(no, request.PersonnelNo, StringComparison.Ordinal) || !string.Equals(name, request.PersonnelName, StringComparison.Ordinal))
        {
            result = Results.Json(new { status="AUTH_CONFLICT", stage="PERSONNEL_VALIDATION", message="Angemeldeter Mitarbeiter stimmt nicht mit dem Vorgang überein. Es wurde keine Materialbuchung gestartet." }, statusCode:403);
            return false;
        }
        result = null; return true;
    }

    private static IResult OperationResult(SeparateOperation tx) => tx.Status switch
    {
        TransactionStatuses.Success => Results.Ok(tx.ToResponse()),
        TransactionStatuses.Rejected => Results.Json(tx.ToResponse(), statusCode:422),
        TransactionStatuses.Conflict or TransactionStatuses.Uncertain or TransactionStatuses.ManualReviewRequired => Results.Json(tx.ToResponse(), statusCode:409),
        _ => Results.Accepted(value: tx.ToResponse())
    };
}
