using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace Fam.Pulverentnahme.Web;

public sealed record FaAbortCorrectionRequest(
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
    decimal CorrectedActualConsumptionKg) : ISeparatePersonnelRequest;

public sealed record FaAbortSourceCheckRequest(
    string PersonnelNo,
    string PersonnelName,
    string TankWarehouse,
    string Article,
    string TankBatch,
    string OrderNo,
    int MaterialPosition,
    decimal ExpectedConsumedKg);

public sealed record FaAbortSourceCheckResponse(
    bool Allowed,
    string Message,
    string SourceWarehouse,
    string SourceBatch,
    string ReportNo,
    string ReportDate,
    string ReportTime);

public sealed record FaAbortResolveSourceRequest(
    string PersonnelNo,
    string PersonnelName,
    string Article,
    string OrderNo,
    int MaterialPosition,
    decimal ExpectedConsumedKg);

public sealed record FaAbortResolveSourceResponse(
    bool Allowed,
    string Message,
    string SourceWarehouse,
    string SourceWarehouseText,
    string SourceBatch,
    decimal TankQuantityKg,
    string Article,
    string ArticleText,
    ArticleRecognitionColorsResult? RecognitionColors,
    string ReportNo,
    string ReportDate,
    string ReportTime);

internal sealed record FaFeedbackReference(
    string Firm,
    string ReportNo,
    string ReportTime,
    string OrderNo,
    string ReportDate,
    int MaterialPosition,
    string Transaction,
    string Article,
    decimal QuantityKg,
    string Warehouse,
    string Batch)
{
    public string Key => $"{Firm}/{OrderNo}/{ReportDate}/{ReportTime}/{ReportNo}";
}

public sealed class FaAbortCorrectionService
{
    public const string Kind = "fa-abort-correction";
    private readonly SeparateOperationStore _store;
    private readonly OxaionClient _oxaion;
    private readonly OxaionOptions _options;
    private readonly FaMaterialService _materials;
    private readonly MachineTankService _tanks;
    private readonly PersonnelService _personnel;
    private readonly FaConsumptionService _faConsumption;

    public FaAbortCorrectionService(
        SeparateOperationStore store,
        OxaionClient oxaion,
        IOptions<OxaionOptions> options,
        FaMaterialService materials,
        MachineTankService tanks,
        PersonnelService personnel,
        FaConsumptionService faConsumption)
    {
        _store = store;
        _oxaion = oxaion;
        _options = options.Value;
        _materials = materials;
        _tanks = tanks;
        _personnel = personnel;
        _faConsumption = faConsumption;
    }

    public Task<SeparateOperation?> GetAsync(string id, CancellationToken ct) => _store.GetAsync(Kind, id, ct);

    public async Task<FaAbortResolveSourceResponse> ResolveSourceAsync(
        FaAbortResolveSourceRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PersonnelNo)
            || string.IsNullOrWhiteSpace(request.Article)
            || string.IsNullOrWhiteSpace(request.OrderNo)
            || request.MaterialPosition <= 0
            || request.ExpectedConsumedKg <= 0m)
            throw new ArgumentException("Mitarbeiter, Artikel, FA, Materialposition und ursprüngliche Verbrauchsmenge sind erforderlich.");

        await using var session = await _oxaion.ConnectAsync(ct);
        try
        {
            var storno = await OpenStornoListAsync(
                session,
                request.PersonnelNo,
                request.OrderNo,
                request.Article,
                request.MaterialPosition,
                ct);

            var feedback = FindUniqueFeedbackByCore(
                storno.List.Xml,
                request.OrderNo,
                request.MaterialPosition,
                request.Article,
                request.ExpectedConsumedKg);

            if (!await _tanks.IsAllowedAsync(feedback.Warehouse, ct))
                throw new ProcessConflictException(
                    $"Die ursprüngliche Oxaion-Rückmeldung verweist auf Lagerort {feedback.Warehouse}. " +
                    "Dieser Lagerort ist in Oxaion ULGSTP nicht als Tanklagerort (LGLGART=02) definiert. Es wurde nichts storniert.");

            var tank = await _tanks.ReadStockAsync(feedback.Warehouse, ct);
            if (tank.Status != MachineStockStatuses.Unique || tank.Rows.Count != 1)
                throw new ProcessConflictException(
                    $"Der aus der ursprünglichen Oxaion-Rückmeldung ermittelte Tank {feedback.Warehouse} ist aktuell nicht eindeutig: {tank.Message} " +
                    "Es wurde nichts storniert.");

            var row = tank.Rows[0];
            if (!row.Article.Equals(request.Article, StringComparison.OrdinalIgnoreCase)
                || row.Batch != feedback.Batch)
                throw new ProcessConflictException(
                    $"Die Tankcharge hat sich seit der ursprünglichen FA-Buchung geändert. " +
                    $"Ursprüngliche Rückmeldung: Tank {feedback.Warehouse} / Mix-Charge {feedback.Batch}. " +
                    $"Aktueller Tankbestand: {row.Article} / Mix-Charge {row.Batch}. " +
                    "Automatische Rückbuchung ist gesperrt. Bitte den Fall in Oxaion prüfen. Es wurde nichts storniert.");

            return new FaAbortResolveSourceResponse(
                true,
                $"Oxaion-Rückmeldung eindeutig zugeordnet: Tank {feedback.Warehouse} / Mix-Charge {feedback.Batch}.",
                feedback.Warehouse,
                feedback.Warehouse,
                feedback.Batch,
                row.QuantityKg,
                row.Article,
                row.ArticleText,
                tank.RecognitionColors,
                feedback.ReportNo,
                feedback.ReportDate,
                feedback.ReportTime);
        }
        finally
        {
            try
            {
                await session.CallAsync("PW22000J", "*CLOSE", new Dictionary<string, string>(), CancellationToken.None);
            }
            catch
            {
                // Read-only preparation cleanup only.
            }
        }
    }

    public async Task<FaAbortSourceCheckResponse> ValidateSourceAsync(
        FaAbortSourceCheckRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PersonnelNo)
            || string.IsNullOrWhiteSpace(request.TankWarehouse)
            || string.IsNullOrWhiteSpace(request.Article)
            || string.IsNullOrWhiteSpace(request.TankBatch)
            || string.IsNullOrWhiteSpace(request.OrderNo)
            || request.MaterialPosition <= 0
            || request.ExpectedConsumedKg <= 0m)
            throw new ArgumentException("Mitarbeiter, Tank, Artikel/Mix, FA, Materialposition und ursprüngliche Verbrauchsmenge sind erforderlich.");

        await using var session = await _oxaion.ConnectAsync(ct);
        try
        {
            var storno = await OpenStornoListAsync(
                session,
                request.PersonnelNo,
                request.OrderNo,
                request.Article,
                request.MaterialPosition,
                ct);

            var feedback = FindUniqueFeedback(
                storno.List.Xml,
                request.OrderNo,
                request.MaterialPosition,
                request.Article,
                request.ExpectedConsumedKg,
                request.TankWarehouse,
                request.TankBatch);

            return new FaAbortSourceCheckResponse(
                true,
                $"Die gültige Oxaion-Rückmeldung gehört zu Tank {feedback.Warehouse} / Mix-Charge {feedback.Batch}. Automatische Jobabbruch-Korrektur ist für diesen Tank zulässig.",
                feedback.Warehouse,
                feedback.Batch,
                feedback.ReportNo,
                feedback.ReportDate,
                feedback.ReportTime);
        }
        finally
        {
            try
            {
                await session.CallAsync("PW22000J", "*CLOSE", new Dictionary<string, string>(), CancellationToken.None);
            }
            catch
            {
                // Read-only validation cleanup only.
            }
        }
    }

    public async Task<SeparateOperation> ExecuteAsync(FaAbortCorrectionRequest request, CancellationToken ct)
    {
        Validate(request);
        var gate = _store.GetLock(Kind, request.ClientOperationId);
        await gate.WaitAsync(ct);
        try
        {
            var json = SeparateOperationStore.SerializeRequest(request);
            var existing = await _store.GetAsync(Kind, request.ClientOperationId, ct);
            if (existing is not null)
            {
                if (existing.RequestJson != json)
                    throw new ProcessConflictException("clientOperationId already belongs to different FA-abort correction data.");
                return existing;
            }

            var tx = new SeparateOperation
            {
                Kind = Kind,
                ClientOperationId = request.ClientOperationId,
                RequestJson = json,
                ExpectedFaConsumedKg = request.ExpectedConsumedKg,
                ExpectedFaMaterialStatus = request.ExpectedMaterialStatus,
                TargetFaConsumedKg = request.CorrectedActualConsumptionKg
            };
            await SaveEventAsync(tx, "CREATED", "FA job-abort correction created.", ct);
            var stornoSent = false;
            var stornoConfirmed = false;
            try
            {
                var employee = await _personnel.ReadExactAsync(request.PersonnelNo, ct);
                if (employee is null || employee.FullName != request.PersonnelName)
                    throw new ProcessConflictException("Mitarbeiter ist in Oxaion nicht mehr eindeutig bestätigt.");

                var tankBefore = await _tanks.ReadStockAsync(request.TankWarehouse, ct);
                if (tankBefore.Status != MachineStockStatuses.Unique || tankBefore.Rows.Count != 1)
                    throw new ProcessConflictException("Tankbestand ist vor dem FA-Storno nicht eindeutig. " + tankBefore.Message);
                var tankRow = tankBefore.Rows[0];
                if (!SameTank(tankRow, request.Article, request.TankBatch, request.TankQuantityKg))
                    throw new ProcessConflictException(
                        $"Tankbestand hat sich seit der Anzeige geändert. Aktuell {tankRow.Article}/{tankRow.Batch}/{tankRow.QuantityKg:0.###} kg.");

                var material = await _materials.FindUniqueAsync(request.OrderNo, request.Article, ct);
                if (material.MaterialPosition != request.MaterialPosition
                    || Math.Abs(material.RequiredKg - request.ExpectedRequiredKg) >= 0.0005m
                    || Math.Abs(material.ConsumedKg - request.ExpectedConsumedKg) >= 0.0005m
                    || material.MaterialStatus != request.ExpectedMaterialStatus)
                    throw new ProcessConflictException(
                        $"FA-Materialposition hat sich seit der Anzeige geändert. Aktuell Pos. {material.MaterialPosition}, " +
                        $"Soll {material.RequiredKg:0.###} kg, gebucht {material.ConsumedKg:0.###} kg, Status {material.MaterialStatus}.");

                if (material.MaterialStatus != 9 || material.ConsumedKg <= 0m)
                    throw new ProcessConflictException(
                        "Der bestätigte Jobabbruch-Storno ist nur für eine komplett abgebuchte Materialposition (Status 9) mit positivem Verbrauch freigegeben.");

                await using var session = await _oxaion.ConnectAsync(ct);
                try
                {
                    var storno = await OpenStornoListAsync(
                        session,
                        request.PersonnelNo,
                        request.OrderNo,
                        request.Article,
                        request.MaterialPosition,
                        ct);
                    var feedback = FindUniqueFeedback(
                        storno.List.Xml,
                        request.OrderNo,
                        request.MaterialPosition,
                        request.Article,
                        request.ExpectedConsumedKg,
                        request.TankWarehouse,
                        request.TankBatch);

                    await SaveEventAsync(tx, "STORNO_TARGET_CONFIRMED",
                        $"Unique valid Oxaion feedback selected: {feedback.Key}, {feedback.QuantityKg:0.###} kg, {feedback.Warehouse}/{feedback.Batch}.", ct);

                    // The captured Oxaion client posts the PW22021R *GETHDR result plus the
                    // selected KEY fields. The previous Merge(header, PW22000J state) incorrectly
                    // sent hundreds of unrelated dialog fields and overwrote header values.
                    var stornoInput = BuildStornoPayload(storno.Header.Dta, storno.Ssid, feedback);
                    tx.Status = TransactionStatuses.SendingToOxaion;
                    stornoSent = true;
                    await SaveEventAsync(tx, "STORNO_SUBMITTING",
                        $"Submitting PW22021R *STORNO for Oxaion feedback {feedback.Key}.", ct);

                    var response = await session.CallAsync(
                        "PW22021R", "*STORNO", stornoInput, ct, allowXmlDeclarationOnly: true);
                    OxaionSession.AssertNoFcod(response);
                    await SaveEventAsync(tx, "STORNO_RESPONSE_RECEIVED",
                        "Oxaion responded to STORNO. Feedback list, FA and tank must all be verified before new MK.", ct);

                    var refreshed = await RefreshStornoListAsync(session, storno.Ssid, ct);
                    if (ParseFeedbacks(refreshed.Xml).Any(x => x.Key == feedback.Key))
                        throw new InvalidOperationException(
                            $"The exact valid Oxaion feedback {feedback.Key} is still in the standard storno list after STORNO. Do not send STORNO again.");

                    var materialAfter = await _materials.FindUniqueAsync(request.OrderNo, request.Article, ct);
                    var expectedTankAfterStorno = request.TankQuantityKg + request.ExpectedConsumedKg;
                    var tankAfter = await _tanks.ReadStockAsync(request.TankWarehouse, ct);
                    if (materialAfter.MaterialPosition != request.MaterialPosition
                        || Math.Abs(materialAfter.ConsumedKg) >= 0.0005m
                        || materialAfter.MaterialStatus != 0)
                        throw new InvalidOperationException(
                            $"Storno feedback disappeared, but FA state is not the confirmed result. " +
                            $"Current consumed {materialAfter.ConsumedKg:0.###} kg, status {materialAfter.MaterialStatus}. Do not retry STORNO.");

                    if (tankAfter.Status != MachineStockStatuses.Unique || tankAfter.Rows.Count != 1
                        || !SameTank(tankAfter.Rows[0], request.Article, request.TankBatch, expectedTankAfterStorno))
                        throw new InvalidOperationException(
                            $"Storno feedback disappeared, but the tank did not return to the expected same mix charge " +
                            $"{request.TankBatch} with {expectedTankAfterStorno:0.###} kg. Do not retry STORNO.");

                    stornoConfirmed = true;
                    tx.Status = TransactionStatuses.Validating;
                    await SaveEventAsync(tx, "STORNO_CONFIRMED",
                        $"Original {request.ExpectedConsumedKg:0.###} kg feedback cancelled; FA 0 kg/status 0 and tank {expectedTankAfterStorno:0.###} kg confirmed.", ct);

                    if (request.CorrectedActualConsumptionKg <= 0.0005m)
                    {
                        tx.Status = TransactionStatuses.Success;
                        await SaveEventAsync(tx, "SUCCESS",
                            "Job-abort correction completed: original feedback cancelled and actual consumption 0.000 kg; no new MK needed.", ct);
                        return tx;
                    }

                    var childId = request.ClientOperationId + "-mk";
                    tx.RelatedOperationId = childId;
                    await SaveEventAsync(tx, "CORRECTED_MK_STARTING",
                        $"Starting corrected MK child operation {childId} for {request.CorrectedActualConsumptionKg:0.###} kg.", ct);
                    var child = await _faConsumption.ExecuteAsync(new FaConsumptionRequest(
                        childId,
                        request.PersonnelNo,
                        request.PersonnelName,
                        request.TankWarehouse,
                        request.TankWarehouseText,
                        request.Article,
                        request.ArticleText,
                        request.TankBatch,
                        expectedTankAfterStorno,
                        request.OrderNo,
                        request.PlannedMachineId,
                        request.MaterialPosition,
                        request.ExpectedRequiredKg,
                        0m,
                        0,
                        request.CorrectedActualConsumptionKg), ct);

                    if (child.Status != TransactionStatuses.Success)
                    {
                        tx.Status = TransactionStatuses.ManualReviewRequired;
                        await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED",
                            $"Der ursprüngliche FA-Verbrauch wurde sicher storniert, aber die neue MK-Buchung ist nicht eindeutig erfolgreich " +
                            $"(Teilvorgang {childId}: {child.Status}/{child.Stage}). Den Gesamtvorgang nicht erneut starten. Teilvorgang prüfen.", ct);
                        return tx;
                    }

                    var finalMaterial = await _materials.FindUniqueAsync(request.OrderNo, request.Article, ct);
                    var expectedFinalTank = expectedTankAfterStorno - request.CorrectedActualConsumptionKg;
                    var finalTank = await _tanks.ReadStockAsync(request.TankWarehouse, ct);
                    if (finalMaterial.MaterialPosition != request.MaterialPosition
                        || Math.Abs(finalMaterial.ConsumedKg - request.CorrectedActualConsumptionKg) >= 0.0005m
                        || finalMaterial.MaterialStatus != 9
                        || finalTank.Status != MachineStockStatuses.Unique
                        || finalTank.Rows.Count != 1
                        || !SameTank(finalTank.Rows[0], request.Article, request.TankBatch, expectedFinalTank))
                    {
                        tx.Status = TransactionStatuses.ManualReviewRequired;
                        await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED",
                            "Storno und neue MK wurden als erfolgreich gemeldet, der kombinierte finale FA-/Tankzustand konnte aber nicht exakt bestätigt werden. Nicht erneut buchen.", ct);
                        return tx;
                    }

                    tx.Status = TransactionStatuses.Success;
                    await SaveEventAsync(tx, "SUCCESS",
                        $"Job-abort correction completed: {request.ExpectedConsumedKg:0.###} kg cancelled, " +
                        $"{request.CorrectedActualConsumptionKg:0.###} kg actual consumption rebooked, same mix charge verified.", ct);
                }
                finally
                {
                    try
                    {
                        await session.CallAsync("PW22000J", "*CLOSE", new Dictionary<string, string>(), CancellationToken.None);
                    }
                    catch { }
                }
            }
            catch (ProcessConflictException ex)
            {
                tx.Status = TransactionStatuses.Conflict;
                await SaveEventAsync(tx, "CONFLICT", ex.Message, ct);
            }
            catch (OxaionRejectedException ex)
            {
                tx.Status = stornoSent ? TransactionStatuses.ManualReviewRequired : TransactionStatuses.Rejected;
                await SaveEventAsync(tx, tx.Status,
                    stornoSent
                        ? $"Oxaion rejected a step after STORNO submission ({ex.Code}). Do not repeat the overall correction; check Oxaion manually. {ex.Message}"
                        : ex.Message, ct);
            }
            catch (OxaionTransportException ex)
            {
                tx.Status = stornoSent ? TransactionStatuses.Uncertain : TransactionStatuses.Conflict;
                await SaveEventAsync(tx, stornoSent ? "UNCERTAIN" : "CONFLICT",
                    stornoSent
                        ? ex.Message + " STORNO may have been processed. Do not retry; use status check/manual review."
                        : ex.Message, ct);
            }
            catch (Exception ex)
            {
                tx.Status = stornoConfirmed || stornoSent
                    ? TransactionStatuses.ManualReviewRequired : TransactionStatuses.Conflict;
                await SaveEventAsync(tx,
                    tx.Status == TransactionStatuses.ManualReviewRequired ? "MANUAL_REVIEW_REQUIRED" : "CONFLICT",
                    ex.Message, ct);
            }
            return tx;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<SeparateOperation> ReconcileAsync(string id, CancellationToken ct)
    {
        var tx = await _store.GetAsync(Kind, id, ct) ?? throw new KeyNotFoundException();
        if (tx.Status == TransactionStatuses.Success) return tx;
        if (!string.IsNullOrWhiteSpace(tx.RelatedOperationId))
        {
            var child = await _faConsumption.GetAsync(tx.RelatedOperationId, ct);
            if (child is not null && child.Status != TransactionStatuses.Success)
                child = await _faConsumption.ReconcileAsync(tx.RelatedOperationId, ct);
            if (child?.Status == TransactionStatuses.Success)
            {
                tx.Status = TransactionStatuses.ManualReviewRequired;
                await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED",
                    $"Der korrigierte MK-Teilvorgang {tx.RelatedOperationId} ist in Oxaion plausibel/erfolgreich; " +
                    "den Gesamtvorgang nach vorherigem unklarem Zustand nicht automatisch auf SUCCESS setzen. FA und Tank manuell prüfen.", ct);
                return tx;
            }
        }
        tx.Status = TransactionStatuses.ManualReviewRequired;
        await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED",
            "Ein Jobabbruch-Storno darf nach unklarem Ausgang nicht automatisch wiederholt oder fortgesetzt werden. " +
            "Originalrückmeldung, FA-Materialposition, Tank-Mix-Charge und gegebenenfalls MK-Teilvorgang in Oxaion prüfen.", ct);
        return tx;
    }

    internal static Dictionary<string, string> BuildStornoPayload(
        IReadOnlyDictionary<string, string> header,
        string ssid,
        FaFeedbackReference feedback)
    {
        // The confirmed JET *STORNO request contains GET​HDR's 26-field DTA plus four
        // report-key fields; ARFAUN and SSID were already present in the GET​HDR result.
        var result = new Dictionary<string, string>(header, StringComparer.Ordinal)
        {
            ["SSID"] = ssid,
            ["ARFIRM"] = feedback.Firm,
            ["ARFAUN"] = feedback.OrderNo,
            ["ARYRML"] = feedback.ReportDate,
            ["ARRMZT"] = feedback.ReportTime,
            ["ARRMNR"] = feedback.ReportNo
        };
        if (string.IsNullOrWhiteSpace(ssid) || string.IsNullOrWhiteSpace(feedback.Firm)
            || string.IsNullOrWhiteSpace(feedback.ReportNo)
            || string.IsNullOrWhiteSpace(feedback.ReportDate)
            || string.IsNullOrWhiteSpace(feedback.ReportTime)
            || string.IsNullOrWhiteSpace(feedback.OrderNo)
            || !header.TryGetValue("ARFAUN", out var headerOrder)
            || !string.Equals(headerOrder, feedback.OrderNo, StringComparison.Ordinal))
            throw new ProcessConflictException("Oxaion-Storno-Header und Rückmeldeschlüssel sind nicht eindeutig kompatibel. Kein Storno ausgeführt.");
        return result;
    }

    internal static FaFeedbackReference FindUniqueFeedbackByCore(
        XDocument xml,
        string orderNo,
        int materialPosition,
        string article,
        decimal quantityKg)
    {
        var eligible = ParseFeedbacks(xml);
        var coreMatches = eligible
            .Where(x => x.OrderNo == orderNo
                && x.MaterialPosition == materialPosition
                && x.Article.Equals(article, StringComparison.OrdinalIgnoreCase)
                && Math.Abs(x.QuantityKg - quantityKg) < 0.0005m)
            .ToList();

        if (coreMatches.Count == 0)
        {
            var candidateSummary = eligible.Count == 1
                ? $" Oxaion-Kandidat: FA={eligible[0].OrderNo}, Pos={eligible[0].MaterialPosition}, Artikel={eligible[0].Article}, Menge={eligible[0].QuantityKg:0.###} kg."
                : "";
            throw new ProcessConflictException(
                $"Keine passende gültige Oxaion-Materialrückmeldung gefunden. Erwartet: FA={orderNo}, Pos={materialPosition}, Artikel={article}, Menge={quantityKg:0.###} kg.{candidateSummary} Es wurde nichts storniert.");
        }

        if (coreMatches.Count != 1)
            throw new ProcessConflictException(
                $"Mehrere ({coreMatches.Count}) gültige Oxaion-Materialrückmeldungen passen zu FA, Position, Artikel und Menge. " +
                "Tank und Mix-Charge können deshalb nicht automatisch eindeutig abgeleitet werden. Es wurde nichts storniert.");

        var feedback = coreMatches[0];
        if (string.IsNullOrWhiteSpace(feedback.Warehouse) || string.IsNullOrWhiteSpace(feedback.Batch))
            throw new ProcessConflictException(
                "Tank/Mix-Charge der ursprünglichen Oxaion-Rückmeldung sind nicht eindeutig lesbar. " +
                "Bitte den Fall in Oxaion prüfen. Es wurde nichts storniert.");

        return feedback;
    }

    internal static FaFeedbackReference FindUniqueFeedback(
        XDocument xml,
        string orderNo,
        int materialPosition,
        string article,
        decimal quantityKg,
        string warehouse,
        string batch)
    {
        // PW22021R *FIRSTLIST is already the standard Oxaion list of valid/stornable
        // feedback rows. Automatic job-abort correction is nevertheless allowed only
        // when the original feedback source still matches the currently scanned tank
        // and mix batch. Returning material to a different current batch is forbidden.
        var eligible = ParseFeedbacks(xml);
        var coreMatches = eligible
            .Where(x => x.OrderNo == orderNo
                && x.MaterialPosition == materialPosition
                && x.Article.Equals(article, StringComparison.OrdinalIgnoreCase)
                && Math.Abs(x.QuantityKg - quantityKg) < 0.0005m)
            .ToList();

        if (coreMatches.Count == 0)
        {
            var candidateSummary = eligible.Count == 1
                ? $" Oxaion-Kandidat: FA={eligible[0].OrderNo}, Pos={eligible[0].MaterialPosition}, Artikel={eligible[0].Article}, Menge={eligible[0].QuantityKg:0.###} kg."
                : "";
            throw new ProcessConflictException(
                $"Keine passende gültige Oxaion-Materialrückmeldung gefunden. Erwartet: FA={orderNo}, Pos={materialPosition}, Artikel={article}, Menge={quantityKg:0.###} kg.{candidateSummary} Es wurde nichts storniert.");
        }

        var exactSourceMatches = coreMatches
            .Where(x => x.Warehouse.Equals(warehouse, StringComparison.OrdinalIgnoreCase)
                && x.Batch == batch)
            .ToList();

        if (exactSourceMatches.Count == 1)
            return exactSourceMatches[0];

        if (coreMatches.Count == 1)
        {
            var original = coreMatches[0];
            if (string.IsNullOrWhiteSpace(original.Warehouse) || string.IsNullOrWhiteSpace(original.Batch))
                throw new ProcessConflictException(
                    "Dieser Fertigungsauftrag kann nicht automatisch rückgebucht werden, weil Tank/Mix-Charge der ursprünglichen Oxaion-Rückmeldung nicht eindeutig lesbar sind. " +
                    "Bitte den Fall in Oxaion prüfen und gegebenenfalls manuell über Lagerbelege korrigieren. Es wurde nichts storniert.");

            throw new ProcessConflictException(
                $"Dieser Fertigungsauftrag kann nicht auf den gescannten Tank zurückgebucht werden, weil sich die Tankcharge seit der ursprünglichen FA-Buchung geändert hat. " +
                $"Ursprüngliche Rückmeldung: Tank {original.Warehouse} / Mix-Charge {original.Batch}. " +
                $"Aktuell gescannt: Tank {warehouse} / Mix-Charge {batch}. " +
                "Automatische Rückbuchung ist gesperrt. Bitte den Fall in Oxaion prüfen und gegebenenfalls manuell über Lagerbelege korrigieren. Es wurde nichts storniert.");
        }

        if (exactSourceMatches.Count == 0)
            throw new ProcessConflictException(
                $"Es gibt {coreMatches.Count} gültige Oxaion-Rückmeldungen für FA, Position, Artikel und Menge, aber keine gehört zum aktuell gescannten Tank {warehouse} / Mix-Charge {batch}. " +
                "Automatische Rückbuchung ist gesperrt. Bitte den Fall in Oxaion prüfen und gegebenenfalls manuell über Lagerbelege korrigieren. Es wurde nichts storniert.");

        throw new ProcessConflictException(
            $"Mehrere ({exactSourceMatches.Count}) gültige Oxaion-Materialrückmeldungen passen zu FA, Position, Menge und Tank/Mix. Automatischer Storno ist gesperrt; bitte den Fall in Oxaion prüfen.");
    }

    internal static IReadOnlyList<FaFeedbackReference> ParseFeedbacks(XDocument xml)
    {
        var rows = new List<FaFeedbackReference>();
        foreach (var row in xml.Descendants("ROW"))
        {
            var key = row.Element("KEY");
            if (key is null) continue;
            string V(string name) => row.Element(name)?.Value.Trim() ?? "";
            string K(string name) => key.Element(name)?.Value.Trim() ?? "";
            var stornoStatus = V("PWARMP.ARSTOR");
            if (string.IsNullOrEmpty(stornoStatus)) stornoStatus = V("ARSTOR");
            if (!string.IsNullOrWhiteSpace(stornoStatus)
                && !string.Equals(stornoStatus, "N", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!int.TryParse(V("PWARMP.ARPOSN"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var position))
                continue;
            var desc = V("_INTERN.WW_TX50");
            var amountMatch = Regex.Match(desc, @"(?<qty>[0-9]+(?:[.,][0-9]+)?)\s*kg", RegexOptions.IgnoreCase);
            if (!amountMatch.Success) continue;
            if (!decimal.TryParse(amountMatch.Groups["qty"].Value.Replace(',', '.'),
                    NumberStyles.Number, CultureInfo.InvariantCulture, out var qty))
                continue;
            var article = desc.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            var source = V("_INTERN.WW_TX70B").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (source.Length < 3) continue;
            rows.Add(new FaFeedbackReference(
                K("ARFIRM"), K("ARRMNR"), K("ARRMZT"), K("ARFAUN"), K("ARYRML"),
                position, V("PWARMP.ARAKKZ"), article, qty, source[1], source[2]));
        }
        return rows;
    }

    private async Task<StornoContext> OpenStornoListAsync(
        OxaionSession session,
        string personnelNo,
        string orderNo,
        string article,
        int materialPosition,
        CancellationToken ct)
    {
        var pos = materialPosition.ToString(CultureInfo.InvariantCulture);
        var penu = PersonnelService.ToOxaionPersonnelNumber(personnelNo);
        var objectKey = orderNo + materialPosition.ToString("00000", CultureInfo.InvariantCulture) + article;
        var load = await session.CallAsync("PW22000J", "*LOADNEW", Dict(
            ("STTXOA", "FAUNPOSN"), ("AMPOSN", pos), ("PCDPOSI", pos),
            ("PCBGNR", orderNo), ("STTOBI", objectKey), ("AMIDNK", article),
            ("PCANWG", "PPS"), ("KEYTYPE", "PWAMA"), ("STANWG", "PPS"),
            ("STORNO", "J"), ("AMFAUN", orderNo), ("ARAKKZ", "MK")), ct);
        OxaionSession.AssertNoFcod(load);
        var state = Merge(load.Dta, Dict(
            ("ARAKKZ", "M*"), ("ARFAUN", orderNo), ("ARPOSN", pos),
            ("ARPENU", penu), ("ARFIRM", _options.Firm)));
        var ston = await session.CallAsync("PW22000J", "*STON", state, ct);
        OxaionSession.AssertNoFcod(ston);
        state = Merge(state, ston.Dta);
        var ssid = Get(state, "SSID");
        if (string.IsNullOrWhiteSpace(ssid))
            throw new InvalidOperationException("PW22000J *STON did not return an SSID.");
        var header = await session.CallAsync("PW22021R", "*GETHDR", state, ct);
        OxaionSession.AssertNoFcod(header);
        var list = await session.CallAsync("PW22021R", "*FIRSTLIST", Dict(
            ("FLD", ""), ("SSID", ssid), ("PFLD", ""), ("mode", "replace")), ct);
        OxaionSession.AssertNoFcod(list);
        if (!list.Xml.Descendants("STOP").Any())
            throw new InvalidOperationException("PW22021R storno list did not return STOP; incomplete list is not accepted.");
        return new StornoContext(ssid, state, header, list);
    }

    private static async Task<OxaionCallResult> RefreshStornoListAsync(
        OxaionSession session, string ssid, CancellationToken ct)
    {
        var u01 = await session.CallAsync("PW22021R", "*GETU01", Dict(("SSID", ssid)), ct);
        OxaionSession.AssertNoFcod(u01);
        var list = await session.CallAsync("PW22021R", "*FIRSTLIST", Dict(
            ("FLD", ""), ("SSID", ssid), ("PFLD", ""), ("mode", "replace")), ct);
        OxaionSession.AssertNoFcod(list);
        if (!list.Xml.Descendants("STOP").Any())
            throw new InvalidOperationException("PW22021R refreshed storno list did not return STOP.");
        return list;
    }

    private static bool SameTank(MachineStockRow row, string article, string batch, decimal quantityKg) =>
        row.Article.Equals(article, StringComparison.OrdinalIgnoreCase)
        && row.Batch == batch
        && Math.Abs(row.QuantityKg - quantityKg) < 0.0005m;

    private static void Validate(FaAbortCorrectionRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.ClientOperationId)
            || string.IsNullOrWhiteSpace(r.PersonnelNo)
            || string.IsNullOrWhiteSpace(r.TankWarehouse)
            || string.IsNullOrWhiteSpace(r.Article)
            || string.IsNullOrWhiteSpace(r.TankBatch)
            || string.IsNullOrWhiteSpace(r.OrderNo)
            || r.MaterialPosition <= 0)
            throw new ArgumentException("Operation, personnel, tank, article/batch, FA and material position are required.");
        if (r.TankQuantityKg <= 0m || r.ExpectedConsumedKg <= 0m)
            throw new ArgumentException("Tank quantity and original consumed quantity must be > 0.");
        if (r.ExpectedMaterialStatus != 9)
            throw new ArgumentException("The confirmed job-abort storno flow requires material status 9.");
        if (r.CorrectedActualConsumptionKg < 0m)
            throw new ArgumentException("Corrected actual consumption must not be negative.");
        if (r.CorrectedActualConsumptionKg >= r.ExpectedConsumedKg - 0.0005m)
            throw new ArgumentException("Corrected actual consumption must be lower than the original booked consumption.");
    }

    private async Task SaveEventAsync(SeparateOperation tx, string stage, string message, CancellationToken ct)
    {
        tx.Stage = stage;
        tx.Message = message;
        tx.Events.Add(new TransactionEvent(DateTimeOffset.UtcNow, stage, message));
        await _store.SaveAsync(tx, ct);
    }

    private sealed record StornoContext(
        string Ssid, Dictionary<string, string> State, OxaionCallResult Header, OxaionCallResult List);

    private static Dictionary<string, string> Dict(params (string Key, string Value)[] values) =>
        values.ToDictionary(x => x.Key, x => x.Value ?? "", StringComparer.Ordinal);

    private static Dictionary<string, string> Merge(
        IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
    {
        var result = new Dictionary<string, string>(a, StringComparer.Ordinal);
        foreach (var item in b) result[item.Key] = item.Value ?? "";
        return result;
    }

    private static string Get(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : "";
}
