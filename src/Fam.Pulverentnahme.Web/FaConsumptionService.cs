using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace Fam.Pulverentnahme.Web;

public sealed class FaConsumptionService
{
    public const string Kind = "fa-consumption";
    private readonly SeparateOperationStore _store;
    private readonly OxaionClient _oxaion;
    private readonly OxaionOptions _options;
    private readonly FaMaterialService _materials;
    private readonly MachineTankService _tanks;
    private readonly PersonnelService _personnel;

    public FaConsumptionService(SeparateOperationStore store, OxaionClient oxaion, IOptions<OxaionOptions> options, FaMaterialService materials, MachineTankService tanks, PersonnelService personnel)
    { _store = store; _oxaion = oxaion; _options = options.Value; _materials = materials; _tanks = tanks; _personnel = personnel; }

    public Task<SeparateOperation?> GetAsync(string id, CancellationToken ct) => _store.GetAsync(Kind, id, ct);

    public async Task<SeparateOperation> ExecuteAsync(FaConsumptionRequest request, CancellationToken ct)
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
                if (existing.RequestJson != json) throw new ProcessConflictException("clientOperationId already belongs to different FA consumption data.");
                return existing;
            }
            var tx = new SeparateOperation
            {
                Kind = Kind, ClientOperationId = request.ClientOperationId, RequestJson = json,
                ExpectedFaConsumedKg = request.ExpectedConsumedKg,
                ExpectedFaMaterialStatus = request.ExpectedMaterialStatus,
                TargetFaConsumedKg = FaMaterialService.TargetConsumed(request.ExpectedConsumedKg, request.AdditionalConsumptionKg)
            };
            await SaveEventAsync(tx, "CREATED", "FA consumption transaction created.", ct);
            try
            {
                var employee = await _personnel.ReadExactAsync(request.PersonnelNo, ct);
                if (employee is null || employee.FullName != request.PersonnelName)
                    throw new ProcessConflictException("Mitarbeiter ist in Oxaion nicht mehr eindeutig bestätigt.");
                var tank = await _tanks.ReadStockAsync(request.TankWarehouse, ct);
                if (tank.Status != MachineStockStatuses.Unique || tank.Rows.Count != 1)
                    throw new ProcessConflictException("Tankbestand ist vor der FA-Buchung nicht mehr eindeutig. " + tank.Message);
                var t = tank.Rows[0];
                if (!t.Article.Equals(request.Article, StringComparison.OrdinalIgnoreCase) || t.Batch != request.TankBatch || Math.Abs(t.QuantityKg - request.TankQuantityKg) >= 0.0005m)
                    throw new ProcessConflictException($"Tankbestand hat sich seit der Anzeige geändert. Aktuell {t.Article}/{t.Batch}/{t.QuantityKg:0.###} kg.");
                if (request.AdditionalConsumptionKg > t.QuantityKg + 0.0005m)
                    throw new ProcessConflictException("Zusätzlicher Verbrauch ist größer als der aktuelle Tankbestand.");

                await using var session = await _oxaion.ConnectAsync(ct);
                var current = await _materials.FindUniqueAsync(session, request.OrderNo, request.Article, ct);
                if (current.MaterialPosition != request.MaterialPosition
                    || Math.Abs(current.RequiredKg - request.ExpectedRequiredKg) >= 0.0005m
                    || Math.Abs(current.ConsumedKg - request.ExpectedConsumedKg) >= 0.0005m
                    || current.MaterialStatus != request.ExpectedMaterialStatus)
                    throw new ProcessConflictException($"Materialposition hat sich seit der Anzeige geändert. Aktuell Pos. {current.MaterialPosition}, Soll {current.RequiredKg:0.###} kg, gebucht {current.ConsumedKg:0.###} kg, Status {current.MaterialStatus} {current.MaterialStatusText}.");
                if (!current.MkBookingAllowed)
                    throw new ProcessConflictException($"Materialposition {current.MaterialPosition} hat Status {current.MaterialStatus} {current.MaterialStatusText}. MK ist nur bei Status 0, 1 oder 8 zulässig. MU wird in dieser App noch nicht ausgeführt.");

                tx.Status = TransactionStatuses.SendingToOxaion;
                await SaveEventAsync(tx, "MK_SUBMITTING", $"Submitting additional {request.AdditionalConsumptionKg:0.###} kg to FA {request.OrderNo}, material position {request.MaterialPosition}.", ct);
                await SubmitMkAsync(session, request, current, tx.TargetFaConsumedKg!.Value, ct);
                await SaveEventAsync(tx, "MK_RESPONSE_RECEIVED",
                    "Oxaion hat die MK-Anfrage ohne FCOD beantwortet. Die Materialposition wird jetzt ausschließlich lesend in frischen Oxaion-Sessions verifiziert.", ct);

                // The successful reference trace proves the final ERP state through PW20201J *READ.
                // A just-finished write can become visible slightly later than the HTTP response, so
                // retry only the read. Never repeat PW22031J *PUTNEW here.
                var verified = await VerifyMkStateAsync(request, tx.TargetFaConsumedKg.Value, ct);
                if (!IsExactMkResult(verified, request.MaterialPosition, tx.TargetFaConsumedKg.Value))
                    throw new InvalidOperationException(
                        $"Oxaion hat auf die MK-Buchung geantwortet, der erwartete Endzustand konnte danach aber nicht eindeutig bestätigt werden. " +
                        $"Aktuell: Materialposition {verified.MaterialPosition}, tatsächlich gebucht {verified.ConsumedKg:0.###} kg, " +
                        $"Status {verified.MaterialStatus} {verified.MaterialStatusText}. Nicht erneut buchen. Zuerst 'Status in Oxaion prüfen' verwenden und bei weiter unklarem Zustand die Produktionsleitung informieren.");

                tx.Status = TransactionStatuses.Success;
                await SaveEventAsync(tx, "SUCCESS", $"FA {request.OrderNo}, material position {request.MaterialPosition}: consumed quantity verified at {verified.ConsumedKg:0.###} kg, status 9.", CancellationToken.None);
            }
            catch (ProcessConflictException ex) { tx.Status = TransactionStatuses.Conflict; await SaveEventAsync(tx, "CONFLICT", ex.Message, ct); }
            catch (OxaionRejectedException ex) { tx.Status = TransactionStatuses.Rejected; await SaveEventAsync(tx, "REJECTED", ex.Message, ct); }
            catch (OxaionTransportException ex) { tx.Status = TransactionStatuses.Uncertain; await SaveEventAsync(tx, "UNCERTAIN", ex.Message, ct); }
            catch (Exception ex) { tx.Status = TransactionStatuses.ManualReviewRequired; await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED", ex.Message, ct); }
            return tx;
        }
        finally { gate.Release(); }
    }

    public async Task<SeparateOperation> ReconcileAsync(string id, CancellationToken ct)
    {
        var tx = await _store.GetAsync(Kind, id, ct) ?? throw new KeyNotFoundException();
        if (tx.Status == TransactionStatuses.Success) return tx;
        var request = tx.ReadRequest<FaConsumptionRequest>();
        try
        {
            var current = await _materials.FindUniqueAsync(request.OrderNo, request.Article, ct);
            var matchesExpectedState = tx.TargetFaConsumedKg is not null
                && current.MaterialPosition == request.MaterialPosition
                && Math.Abs(current.ConsumedKg - tx.TargetFaConsumedKg.Value) < 0.0005m
                && current.MaterialStatus == 9;
            tx.Status = TransactionStatuses.ManualReviewRequired;
            await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED", matchesExpectedState
                ? $"Der aktuelle Oxaion-Zustand passt zur angeforderten MK-Buchung (Pos. {current.MaterialPosition}, gebucht {current.ConsumedKg:0.###} kg, Status 9), beweist ohne eindeutige Oxaion-Transaktionsreferenz aber nicht sicher, dass genau dieser WebApp-Vorgang die Änderung erzeugt hat. Nicht erneut buchen; manuell prüfen."
                : $"FA-Materialposition beweist den angeforderten MK-Ausgang nicht. Aktuell Pos. {current.MaterialPosition}, gebucht {current.ConsumedKg:0.###} kg, Status {current.MaterialStatus}. Nicht erneut buchen; manuell prüfen.", ct);
        }
        catch (Exception ex)
        {
            tx.Status = TransactionStatuses.ManualReviewRequired;
            await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED", ex.Message, ct);
        }
        return tx;
    }

    private async Task<FaMaterialPositionResult> VerifyMkStateAsync(FaConsumptionRequest request, decimal targetTotal, CancellationToken ct)
    {
        FaMaterialPositionResult? last = null;
        // First read immediately, then three short read-only waits. The write is never repeated.
        int[] delaysMs = [0, 250, 750, 1500];
        foreach (var delayMs in delaysMs)
        {
            if (delayMs > 0) await Task.Delay(delayMs, ct);
            last = await _materials.FindUniqueAsync(request.OrderNo, request.Article, ct);
            if (IsExactMkResult(last, request.MaterialPosition, targetTotal)) return last;
        }
        return last ?? throw new InvalidOperationException("FA-Materialposition konnte nach der MK-Antwort nicht erneut gelesen werden.");
    }

    internal static bool IsExactMkResult(FaMaterialPositionResult result, int position, decimal targetTotal) =>
        result.MaterialPosition == position
        && Math.Abs(result.ConsumedKg - targetTotal) < 0.0005m
        && result.MaterialStatus == 9;

    private async Task SubmitMkAsync(OxaionSession session, FaConsumptionRequest r, FaMaterialPositionResult current, decimal targetTotal, CancellationToken ct)
    {
        var pos = r.MaterialPosition.ToString(CultureInfo.InvariantCulture);
        var penu = PersonnelService.ToOxaionPersonnelNumber(r.PersonnelNo);
        var objectKey = r.OrderNo + r.MaterialPosition.ToString("00000", CultureInfo.InvariantCulture) + r.Article;
        var load = await session.CallAsync("PW22000J", "*LOADNEW", Dict(
            ("STTXOA", "FAUNPOSN"), ("AMPOSN", pos), ("PCDPOSI", pos), ("PCBGNR", r.OrderNo),
            ("STTOBI", objectKey), ("AMIDNK", r.Article), ("PCANWG", "PPS"), ("KEYTYPE", "PWAMA"),
            ("STANWG", "PPS"), ("STORNO", "N"), ("AMFAUN", r.OrderNo), ("ARAKKZ", "MK")), ct);
        OxaionSession.AssertNoFcod(load);

        var chkInput = Merge(load.Dta, Dict(
            ("ARAKKZ", "MK"), ("TX_AKKZ", "Materialkomplettentnahme"), ("AKKZBZ", "Materialkomplettentnahme"),
            ("ARFAUN", r.OrderNo), ("ARPOSN", pos), ("ARPENU", penu),
            ("AMFAUN", r.OrderNo), ("AMPOSN", pos), ("AMIDNK", r.Article), ("AMFIRM", _options.Firm)));
        var chk = await session.CallAsync("PW22000J", "*CHK", chkInput, ct);
        OxaionSession.AssertNoFcod(chk);

        var loaded = await session.CallAsync("PW22031J", "*LOAD", Dict(("NOHWPgm", "PW22031"), ("NoHints", "")), ct);
        OxaionSession.AssertNoFcod(loaded);
        var newInput = Merge(loaded.Dta, chk.Dta);
        foreach (var x in Dict(("ARAKKZ", "MK"), ("TX_AKKZ", "Materialkomplettentnahme"), ("ARFAUN", r.OrderNo),
                     ("ARPOSN", pos), ("ARPENU", penu), ("ARFIRM", _options.Firm), ("ARIDNK", r.Article))) newInput[x.Key] = x.Value;
        var created = await session.CallAsync("PW22031J", "*NEW", newInput, ct);
        OxaionSession.AssertNoFcod(created);
        var state = Merge(newInput, created.Dta);
        foreach (var x in Dict(
                     ("ARAKKZ", "MK"), ("TX_AKKZ", "Materialkomplettentnahme"), ("ARFAUN", r.OrderNo),
                     ("ARPOSN", pos), ("ARPENU", penu), ("ARFIRM", _options.Firm), ("ARIDNK", r.Article),
                     ("I_ARIDNK", r.Article), ("I_TX_IDNK", r.Article), ("ARLAGO", r.TankWarehouse),
                     ("TX_LAGO", r.TankWarehouseText), ("ARLAPL", ""), ("ARPONR", r.TankBatch),
                     ("ARVBME2", FormatQty(targetTotal)), ("ARBGDT", DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                     ("ARVBMK", "KGM"), ("ARMEKZ", "KGM"), ("TX_INKT02", r.ArticleText), ("TX_IDNK02", r.ArticleText))) state[x.Key] = x.Value;

        var sn = await session.CallAsync("PW22031J", "*SNPFLICHT", state, ct);
        OxaionSession.AssertNoFcod(sn);
        var tcode = FirstText(sn.Xml, "TCODE");
        if (!string.Equals(tcode, "ELSE", StringComparison.Ordinal))
            throw new ProcessConflictException(
                $"Oxaion fordert vor der MK-Buchung einen nicht bestätigten Zusatzdialog an (TCODE={tcode}). " +
                "Es wurde keine PW22031J *PUTNEW-Buchung gesendet. Vorgang in Oxaion prüfen.");

        var put = await session.CallAsync("PW22031J", "*PUTNEW", state, ct);
        OxaionSession.AssertNoFcod(put);
    }

    private static void Validate(FaConsumptionRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.ClientOperationId) || string.IsNullOrWhiteSpace(r.PersonnelNo)
            || string.IsNullOrWhiteSpace(r.TankWarehouse) || string.IsNullOrWhiteSpace(r.Article)
            || string.IsNullOrWhiteSpace(r.TankBatch) || string.IsNullOrWhiteSpace(r.OrderNo) || r.MaterialPosition <= 0)
            throw new ArgumentException("Operation, personnel, tank, article/batch, FA and material position are required.");
        if (r.TankQuantityKg <= 0 || r.AdditionalConsumptionKg <= 0) throw new ArgumentException("Tank quantity and additional consumption must be > 0.");
        if (r.AdditionalConsumptionKg > r.TankQuantityKg + 0.0005m) throw new ArgumentException("Additional consumption exceeds prepared tank stock.");
        if (!FaMaterialService.MkStatusAllowed(r.ExpectedMaterialStatus))
            throw new ArgumentException("Normal MK booking is allowed only for prepared material status 0, 1 or 8. MU is intentionally not implemented yet.");
    }

    private async Task SaveEventAsync(SeparateOperation tx, string stage, string message, CancellationToken ct)
    { tx.Stage = stage; tx.Message = message; tx.Events.Add(new TransactionEvent(DateTimeOffset.UtcNow, stage, message)); await _store.SaveAsync(tx, ct); }
    private static Dictionary<string, string> Dict(params (string Key, string Value)[] values) => values.ToDictionary(x => x.Key, x => x.Value ?? "", StringComparer.Ordinal);
    private static Dictionary<string, string> Merge(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
    { var r = new Dictionary<string, string>(a, StringComparer.Ordinal); foreach (var x in b) r[x.Key] = x.Value ?? ""; return r; }
    private static string FirstText(XDocument xml, string name) => xml.Descendants(name).FirstOrDefault()?.Value.Trim() ?? "";
    private static string FormatQty(decimal v) => v.ToString("0.000", CultureInfo.GetCultureInfo("de-AT"));
}
