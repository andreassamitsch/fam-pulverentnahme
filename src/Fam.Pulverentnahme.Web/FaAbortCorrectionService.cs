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
                    var storno = await OpenStornoListAsync(session, request, ct);
                    var feedback = FindUniqueFeedback(
                        storno.List.Xml,
                        request.OrderNo,
                        request.MaterialPosition,
                        request.Article,
                        request.ExpectedConsumedKg,
                        request.TankWarehouse,
                        request.TankBatch);

                    await SaveEventAsync(tx, "STORNO_TARGET_CONFIRMED",
                        $"Unique Oxaion feedback selected for cancellation: {feedback.Key}, {feedback.QuantityKg:0.###} kg, {feedback.Warehouse}/{feedback.Batch}.", ct);

                    var stornoInput = Merge(storno.Header.Dta, storno.State);
                    stornoInput["SSID"] = storno.Ssid;
                    stornoInput["ARFIRM"] = feedback.Firm;
                    stornoInput["ARFAUN"] = feedback.OrderNo;
                    stornoInput["ARYRML"] = feedback.ReportDate;
                    stornoInput["ARRMZT"] = feedback.ReportTime;
                    stornoInput["ARRMNR"] = feedback.ReportNo;

                    tx.Status = TransactionStatuses.SendingToOxaion;
                    stornoSent = true;
                    await SaveEventAsync(tx, "STORNO_SUBMITTING",
                        $"Submitting PW22021R *STORNO for Oxaion feedback {feedback.Key}.", ct);

                    var response = await session.CallAsync(
                        "PW22021R", "*STORNO", stornoInput, ct, allowXmlDeclarationOnly: true);
                    OxaionSession.AssertNoFcod(response);
                    await SaveEventAsync(tx, "STORNO_RESPONSE_RECEIVED",
                        "Oxaion accepted the STORNO call. The result is not trusted until feedback, FA and tank are re-read.", ct);

                    var refreshed = await RefreshStornoListAsync(session, storno.Ssid, ct);
                    var remaining = ParseFeedbacks(refreshed.Xml);
                    if (remaining.Any(x => x.Key == feedback.Key))
                        throw new InvalidOperationException(
                            $"The exact Oxaion feedback {feedback.Key} is still present after STORNO. Do not send STORNO again.");

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
                        $"Original {request.ExpectedConsumedKg:0.###} kg feedback was cancelled exactly; FA is 0 kg/status 0 and tank restored to {expectedTankAfterStorno:0.###} kg.", ct);

                    if (request.CorrectedActualConsumptionKg <= 0.0005m)
                    {
                        tx.Status = TransactionStatuses.Success;
                        await SaveEventAsync(tx, "SUCCESS",
                            "Job-abort correction completed: original feedback cancelled and corrected actual consumption is 0.000 kg, so no new MK booking was required.", ct);
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
                tx.Status = stornoSent
                    ? TransactionStatuses.ManualReviewRequired
                    : TransactionStatuses.Rejected;
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
                    ? TransactionStatuses.ManualReviewRequired
                    : TransactionStatuses.Conflict;
                await SaveEventAsync(tx, tx.Status == TransactionStatuses.ManualReviewRequired ? "MANUAL_REVIEW_REQUIRED" : "CONFLICT",
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
                    $"Der korrigierte MK-Teilvorgang {tx.RelatedOperationId} ist in Oxaion plausibel/erfolgreich, " +
                    "der Gesamtvorgang wird nach vorherigem unklarem Zustand trotzdem nicht automatisch auf SUCCESS gesetzt. FA und Tank manuell final prüfen.", ct);
                return tx;
            }
        }

        tx.Status = TransactionStatuses.ManualReviewRequired;
        await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED",
            "Ein Jobabbruch-Storno darf nach unklarem Ausgang nicht automatisch wiederholt oder automatisch fortgesetzt werden. " +
            "Originalrückmeldung, FA-Materialposition, Tank-Mix-Charge und gegebenenfalls den MK-Teilvorgang in Oxaion prüfen.", ct);
        return tx;
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
        var matches = ParseFeedbacks(xml)
            .Where(x => x.OrderNo == orderNo
                && x.MaterialPosition == materialPosition
                && x.Article.Equals(article, StringComparison.OrdinalIgnoreCase)
                && Math.Abs(x.QuantityKg - quantityKg) < 0.0005m
                && x.Warehouse.Equals(warehouse, StringComparison.OrdinalIgnoreCase)
                && x.Batch == batch)
            .ToList();

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new ProcessConflictException(
                "Keine eindeutig passende Oxaion-Materialrückmeldung zum Stornieren gefunden. Es wurde nichts storniert."),
            _ => throw new ProcessConflictException(
                $"Mehrere ({matches.Count}) passende Oxaion-Materialrückmeldungen gefunden. Automatischer Storno ist gesperrt.")
        };
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

            var posText = V("PWARMP.ARPOSN");
            if (!int.TryParse(posText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var position))
                continue;

            var desc = V("_INTERN.WW_TX50");
            var amountMatch = Regex.Match(desc, @"(?<qty>[0-9]+(?:[.,][0-9]+)?)\s*kg", RegexOptions.IgnoreCase);
            if (!amountMatch.Success) continue;
            var qtyText = amountMatch.Groups["qty"].Value.Replace(',', '.');
            if (!decimal.TryParse(qtyText, NumberStyles.Number, CultureInfo.InvariantCulture, out var qty))
                continue;
            var article = desc.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

            var source = V("_INTERN.WW_TX70B")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (source.Length < 3) continue;

            rows.Add(new FaFeedbackReference(
                K("ARFIRM"),
                K("ARRMNR"),
                K("ARRMZT"),
                K("ARFAUN"),
                K("ARYRML"),
                position,
                V("PWARMP.ARAKKZ"),
                article,
                qty,
                source[1],
                source[2]));
        }
        return rows;
    }

    private async Task<StornoContext> OpenStornoListAsync(
        OxaionSession session,
        FaAbortCorrectionRequest r,
        CancellationToken ct)
    {
        var pos = r.MaterialPosition.ToString(CultureInfo.InvariantCulture);
        var penu = PersonnelService.ToOxaionPersonnelNumber(r.PersonnelNo);
        var objectKey = r.OrderNo + r.MaterialPosition.ToString("00000", CultureInfo.InvariantCulture) + r.Article;
        var load = await session.CallAsync("PW22000J", "*LOADNEW", Dict(
            ("STTXOA", "FAUNPOSN"),
            ("AMPOSN", pos),
            ("PCDPOSI", pos),
            ("PCBGNR", r.OrderNo),
            ("STTOBI", objectKey),
            ("AMIDNK", r.Article),
            ("PCANWG", "PPS"),
            ("KEYTYPE", "PWAMA"),
            ("STANWG", "PPS"),
            ("STORNO", "J"),
            ("AMFAUN", r.OrderNo),
            ("ARAKKZ", "MK")), ct);
        OxaionSession.AssertNoFcod(load);

        var state = Merge(load.Dta, Dict(
            ("ARAKKZ", "M*"),
            ("ARFAUN", r.OrderNo),
            ("ARPOSN", pos),
            ("ARPENU", penu),
            ("ARFIRM", _options.Firm)));
        var ston = await session.CallAsync("PW22000J", "*STON", state, ct);
        OxaionSession.AssertNoFcod(ston);
        state = Merge(state, ston.Dta);

        var ssid = Get(state, "SSID");
        if (string.IsNullOrWhiteSpace(ssid))
            throw new InvalidOperationException("PW22000J *STON did not return an SSID.");

        var header = await session.CallAsync("PW22021R", "*GETHDR", state, ct);
        OxaionSession.AssertNoFcod(header);
        var list = await session.CallAsync("PW22021R", "*FIRSTLIST", Dict(
            ("FLD", ""),
            ("SSID", ssid),
            ("PFLD", ""),
            ("mode", "replace")), ct);
        OxaionSession.AssertNoFcod(list);
        if (!list.Xml.Descendants("STOP").Any())
            throw new InvalidOperationException("PW22021R storno list did not return STOP; incomplete list is not accepted.");

        return new StornoContext(ssid, state, header, list);
    }

    private static async Task<OxaionCallResult> RefreshStornoListAsync(
        OxaionSession session,
        string ssid,
        CancellationToken ct)
    {
        var u01 = await session.CallAsync("PW22021R", "*GETU01", Dict(("SSID", ssid)), ct);
        OxaionSession.AssertNoFcod(u01);
        var list = await session.CallAsync("PW22021R", "*FIRSTLIST", Dict(
            ("FLD", ""),
            ("SSID", ssid),
            ("PFLD", ""),
            ("mode", "replace")), ct);
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
        string Ssid,
        Dictionary<string, string> State,
        OxaionCallResult Header,
        OxaionCallResult List);

    private static Dictionary<string, string> Dict(params (string Key, string Value)[] values) =>
        values.ToDictionary(x => x.Key, x => x.Value ?? "", StringComparer.Ordinal);

    private static Dictionary<string, string> Merge(
        IReadOnlyDictionary<string, string> a,
        IReadOnlyDictionary<string, string> b)
    {
        var result = new Dictionary<string, string>(a, StringComparer.Ordinal);
        foreach (var item in b) result[item.Key] = item.Value ?? "";
        return result;
    }

    private static string Get(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : "";
}
