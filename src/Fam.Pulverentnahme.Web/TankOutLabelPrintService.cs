using System.Globalization;
using System.Xml.Linq;

namespace Fam.Pulverentnahme.Web;

public sealed class TankOutLabelPrintService
{
    public const string Kind = "tank-out-label-print";

    private readonly SeparateOperationStore _store;
    private readonly OxaionClient _oxaion;

    public TankOutLabelPrintService(SeparateOperationStore store, OxaionClient oxaion)
    {
        _store = store;
        _oxaion = oxaion;
    }

    public Task<SeparateOperation?> GetAsync(string id, CancellationToken ct) =>
        _store.GetAsync(Kind, id, ct);

    public async Task<SeparateOperation> ExecuteAsync(TankOutLabelPrintRequest request, CancellationToken ct)
    {
        Validate(request);

        var gate = _store.GetLock(Kind, request.ClientOperationId);
        await gate.WaitAsync(ct);
        try
        {
            var requestJson = SeparateOperationStore.SerializeRequest(request);
            var existing = await _store.GetAsync(Kind, request.ClientOperationId, ct);
            if (existing is not null)
            {
                if (!string.Equals(existing.RequestJson, requestJson, StringComparison.Ordinal))
                    throw new ProcessConflictException("clientOperationId already belongs to different label-print data.");
                return existing;
            }

            var parent = await _store.GetAsync(TankOutService.Kind, request.TankOutOperationId, ct)
                ?? throw new ProcessConflictException("Der zugehörige Tank-Auslagerungsvorgang wurde nicht gefunden.");

            if (!string.Equals(parent.Status, TransactionStatuses.Success, StringComparison.Ordinal))
                throw new ProcessConflictException("Etiketten dürfen erst nach eindeutig erfolgreicher Tank-Auslagerung gedruckt werden.");
            if (string.IsNullOrWhiteSpace(parent.DocumentNo) || parent.HeaderDta is null)
                throw new ProcessConflictException("Der erfolgreiche Tank-Auslagerungsvorgang enthält keinen verifizierbaren Oxaion-Beleg.");

            var parentRequest = parent.ReadRequest<TankOutRequest>();
            if (!string.Equals(parentRequest.PersonnelNo, request.PersonnelNo, StringComparison.Ordinal)
                || !string.Equals(parentRequest.PersonnelName, request.PersonnelName, StringComparison.Ordinal))
                throw new ProcessConflictException("Mitarbeiter des Druckauftrags stimmt nicht mit dem abgeschlossenen Tank-Auslagerungsvorgang überein.");

            var tx = new SeparateOperation
            {
                Kind = Kind,
                ClientOperationId = request.ClientOperationId,
                RequestJson = requestJson,
                DocumentNo = parent.DocumentNo,
                HeaderDta = new Dictionary<string, string>(parent.HeaderDta, StringComparer.Ordinal),
                RelatedOperationId = request.TankOutOperationId
            };
            await SaveEventAsync(tx, "CREATED",
                $"Etikettendruck für Tank-Auslagerung {request.TankOutOperationId} angelegt ({request.LabelCount} Stück).", ct);

            var runSubmitted = false;
            var documentOpened = false;
            await using var session = await _oxaion.ConnectAsync(ct);
            try
            {
                var movement = await ReadUniqueTargetMovementAsync(session, parent, parentRequest, ct);
                documentOpened = true;

                await SaveEventAsync(tx, "LABEL_TARGET_CONFIRMED",
                    $"Zielbewegung LE auf {movement.Warehouse}/{movement.StorageBin} · Charge {movement.Batch} · {movement.Quantity:0.###} kg eindeutig bestätigt.", ct);

                var positionKey = PositionKey(parent.DocumentNo!, parentRequest.Article, movement.Timestamp);

                var details = await session.CallAsync("LB31004R", "", positionKey, ct);
                OxaionSession.AssertNoFcod(details);
                ValidateLabelMovementDetails(details.Dta, parentRequest, movement, parent.DocumentNo!);
                await SaveEventAsync(tx, "LABEL_POSITION_LOADED",
                    $"LB31004R bestätigte die Zielbewegung {parent.DocumentNo}/Pos. 1/{movement.Timestamp}.", ct);

                OxaionSession.AssertNoFcod(await session.CallAsync(
                    "LB20090J",
                    "*CHKPOPUP",
                    Merge(positionKey, Dict(("SSID", GetCurrentListSsid(parent.HeaderDta!)))),
                    ct));

                var labelCall = await session.CallAsync("LB20100J", "*CALLA4ETI", positionKey, ct);
                OxaionSession.AssertNoFcod(labelCall);
                if (!string.Equals(Get(labelCall.Dta, "PGMN"), "EK99103R", StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(Get(labelCall.Dta, "SSID")))
                    throw new ProcessConflictException("Oxaion hat für den bestätigten Lageretikett-Druck nicht EK99103R geöffnet.");

                var labelLoad = await session.CallAsync("EK99103R", "*LOAD",
                    Merge(labelCall.Dta, Merge(positionKey, Dict(("WITH_DS", "*YES")))), ct);
                OxaionSession.AssertNoFcod(labelLoad);
                ValidateLabelLoad(labelLoad.Dta, parent.DocumentNo!, movement.Timestamp);

                var quantityText = request.LabelCount.ToString(CultureInfo.InvariantCulture);
                var printCfg = await session.CallAsync("EK99103R", "*PRINTCFG", Dict(
                    ("MENGE", quantityText),
                    ("LFNR", parent.DocumentNo!),
                    ("BGZT", movement.Timestamp),
                    ("POSI", "1")), ct);
                OxaionSession.AssertNoFcod(printCfg);
                if (!string.Equals(Get(printCfg.Dta, "MENGE"), quantityText, StringComparison.Ordinal)
                    || !string.Equals(Get(printCfg.Dta, "LFNR"), parent.DocumentNo, StringComparison.Ordinal)
                    || !string.Equals(Get(printCfg.Dta, "BGZT"), movement.Timestamp, StringComparison.Ordinal)
                    || !string.Equals(Get(printCfg.Dta, "POSI"), "1", StringComparison.Ordinal))
                    throw new ProcessConflictException("Oxaion hat die gewünschte Etikettenanzahl bzw. Lagerposition nicht eindeutig bestätigt.");

                var defaults = await session.CallAsync("MN50100J", "*GET", null, ct);
                OxaionSession.AssertNoFcod(defaults);
                if (!string.Equals(Get(defaults.Dta, "JOBN"), "EK99102J", StringComparison.Ordinal)
                    || !string.Equals(Get(defaults.Dta, "PGMN"), "EK99102J", StringComparison.Ordinal))
                    throw new ProcessConflictException("Oxaion-Druckdialog lieferte nicht die aufgezeichnete EK99102J-Konfiguration.");

                var table = await session.CallAsync("MN50100J", "*GETTABLE", null, ct);
                OxaionSession.AssertNoFcod(table);
                var printRow = FindUniqueWarehouseLabelPrintConfig(table.Xml);
                var tablePayload = BuildPrintTablePayload(printRow);

                OxaionSession.AssertNoFcod(await session.CallAsync(
                    "MN50100J", "*HIDEDLG", null, ct, allowXmlDeclarationOnly: true));

                var checkInput = new Dictionary<string, string>(defaults.Dta, StringComparer.Ordinal)
                {
                    ["CHKHIDE"] = "N",
                    ["UXTJSC"] = "00.00"
                };
                OxaionSession.AssertNoFcod(await session.CallAsync("MN50100J", "*CHECK", checkInput, ct));
                OxaionSession.AssertNoFcod(await session.CallAsync("MN50100J", "*CHECKTBL", tablePayload, ct));
                OxaionSession.AssertNoFcod(await session.CallAsync("MN50100J", "*PUTTBL", tablePayload, ct));

                await SaveEventAsync(tx, "LABEL_PRINT_SUBMITTING",
                    $"Druckauftrag für {request.LabelCount} Etikett(en) wird an Oxaion EK99102J übergeben.", ct);
                runSubmitted = true;
                var run = await session.CallAsync("MN50100J", "*RUN", checkInput, ct);
                OxaionSession.AssertNoFcod(run);

                tx.Status = TransactionStatuses.Success;
                await SaveEventAsync(tx, "SUCCESS",
                    $"Druckauftrag für {request.LabelCount} Etikett(en) wurde an Oxaion übergeben. Physische Druckausgabe wird von der Druckwarteschlange ausgeführt.", ct);
            }
            catch (ProcessConflictException ex)
            {
                tx.Status = TransactionStatuses.Conflict;
                await SaveEventAsync(tx, "CONFLICT", ex.Message, ct);
            }
            catch (OxaionRejectedException ex)
            {
                tx.Status = TransactionStatuses.Rejected;
                await SaveEventAsync(tx, "REJECTED",
                    $"Oxaion hat den Etikettendruck abgelehnt: {ex.Code}. Die erfolgreiche Tank-Auslagerung bleibt unverändert.", ct);
            }
            catch (OxaionTransportException ex)
            {
                tx.Status = runSubmitted ? TransactionStatuses.Uncertain : TransactionStatuses.Rejected;
                await SaveEventAsync(tx, runSubmitted ? "UNCERTAIN" : "REJECTED",
                    runSubmitted
                        ? "Der Druckauftrag wurde an MN50100J *RUN gesendet, aber die Antwort ist unklar. Drucker/Warteschlange prüfen und nicht blind erneut drucken."
                        : $"Etikettendruck wurde vor dem eigentlichen Drucklauf abgebrochen: {ex.Message}. Die erfolgreiche Tank-Auslagerung bleibt unverändert.",
                    ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                tx.Status = runSubmitted ? TransactionStatuses.ManualReviewRequired : TransactionStatuses.Rejected;
                await SaveEventAsync(tx, runSubmitted ? "MANUAL_REVIEW_REQUIRED" : "REJECTED",
                    runSubmitted
                        ? $"Druckausgang unklar nach Übergabe an Oxaion: {ex.Message} Drucker/Warteschlange prüfen; nicht blind erneut drucken."
                        : $"Etikettendruck wurde nicht gestartet: {ex.Message} Die erfolgreiche Tank-Auslagerung bleibt unverändert.",
                    ct);
            }
            finally
            {
                if (documentOpened)
                {
                    try
                    {
                        var end = await session.CallAsync(
                            "LB20100J",
                            "*END",
                            Dict(("KOBGNR", parent.DocumentNo!), ("KEYTYPE", "LKOPF")),
                            CancellationToken.None);
                        OxaionSession.AssertNoFcod(end);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        if (tx.Status == TransactionStatuses.Success)
                        {
                            tx.Status = TransactionStatuses.ManualReviewRequired;
                            await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED",
                                $"Druckauftrag wurde an Oxaion übergeben, aber die Belegansicht {parent.DocumentNo} konnte danach nicht sicher geschlossen werden. Nicht erneut drucken; Oxaion-Sperre und Druckwarteschlange prüfen. {ex.Message}",
                                CancellationToken.None);
                        }
                    }
                }
            }

            return tx;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<MovementRow> ReadUniqueTargetMovementAsync(
        OxaionSession session,
        SeparateOperation parent,
        TankOutRequest request,
        CancellationToken ct)
    {
        var open = await session.CallAsync("LB20100J", "*OPEN", Dict(
            ("KOBGNR", parent.DocumentNo!),
            ("KEYTYPE", "LKOPF"),
            ("noAutCheck", "")), ct);
        OxaionSession.AssertNoFcod(open);

        var shortResult = await session.CallAsync("LB20090J", "*SHORT", Merge(parent.HeaderDta!, Dict(
            ("PSANWG", "LBS"),
            ("PSBGNR", parent.DocumentNo!),
            ("KEYTYPE", "C_LKOPF"))), ct);
        OxaionSession.AssertNoFcod(shortResult);
        var ssid = Get(shortResult.Dta, "SSID");
        if (string.IsNullOrWhiteSpace(ssid))
            throw new InvalidOperationException("LB20090J *SHORT did not return SSID for label printing.");

        // Keep the list SSID in the header state for the trace-confirmed LB20090J *CHKPOPUP call.
        parent.HeaderDta!["SSID"] = ssid;

        OxaionSession.AssertNoFcod(await session.CallAsync("LB20110R", "*GETHDR", Dict(("SSID", ssid)), ct));
        var list = await session.CallAsync("LB20110R", "*FIRSTLIST", Dict(
            ("FLD", ""),
            ("PFLD", ""),
            ("SSID", ssid),
            ("mode", "replace")), ct);
        OxaionSession.AssertNoFcod(list);
        if (!MachineStockService.HasStop(list.Xml))
            throw new InvalidOperationException("LB20110R label-source list did not return STOP.");

        var rows = MixBookingService.ParseMovements(list.Xml);
        var weighedKg = TankOutService.RoundKg(request.WeighedQuantityKg ?? request.QuantityKg);
        return FindUniqueLabelMovement(rows, request.Article, request.Batch, request.TargetWarehouse, request.TargetStorageBin, weighedKg);
    }

    internal static MovementRow FindUniqueLabelMovement(
        IReadOnlyList<MovementRow> rows,
        string article,
        string batch,
        string targetWarehouse,
        string targetStorageBin,
        decimal quantityKg)
    {
        var matches = rows.Where(row =>
            row.Position == "1"
            && row.BookingKey == "LE"
            && row.Article.Equals(article, StringComparison.OrdinalIgnoreCase)
            && row.Batch == batch
            && row.Warehouse.Equals(targetWarehouse, StringComparison.OrdinalIgnoreCase)
            && string.Equals(row.StorageBin ?? "", targetStorageBin ?? "", StringComparison.Ordinal)
            && Math.Abs(row.Quantity - quantityKg) < 0.0005m
            && !string.IsNullOrWhiteSpace(row.Timestamp)).ToList();

        if (matches.Count != 1)
            throw new ProcessConflictException(
                $"Die Zielbewegung LE für den Etikettendruck wurde im erfolgreichen Lagerbeleg nicht eindeutig gefunden (Treffer: {matches.Count}).");

        return matches[0];
    }

    internal static XElement FindUniqueWarehouseLabelPrintConfig(XDocument xml)
    {
        var matches = xml.Descendants("ROW")
            .Where(row =>
                Value(row, "PRT") == "J"
                && Value(row, "PRTF") == "*EK99102P"
                && Value(row, "BEZC") == "Etikett Wareneingang")
            .ToList();

        if (matches.Count != 1)
            throw new ProcessConflictException(
                $"Die aufgezeichnete Oxaion-Druckkonfiguration '*EK99102P / Etikett Wareneingang' wurde nicht eindeutig gefunden (Treffer: {matches.Count}).");

        return new XElement(matches[0]);
    }

    internal static Dictionary<string, string> BuildPrintTablePayload(XElement row)
    {
        return Dict(
            ("UARC", Value(row, "UARC")),
            ("DBLA", Value(row, "DBLA")),
            ("IUARC", Value(row, "IUARC")),
            ("UGSAVE", Value(row, "UGSAVE")),
            ("CTYC", Value(row, "CTYC")),
            ("PRTF", Value(row, "PRTF")),
            ("PRT", Value(row, "PRT")),
            ("UGCTYC", Value(row, "UGCTYC")),
            ("OFLW", Value(row, "OFLW")),
            ("UGPASO", Value(row, "UGPASO")),
            ("TMPT", Value(row, "TMPT")),
            ("FORA", Value(row, "FORA")),
            ("SSID", ""),
            ("PRPT", Value(row, "PRPT")),
            ("PG", Value(row, "PG")),
            ("PGMN", ""),
            ("UGOUTQ", Value(row, "UGOUTQ")),
            ("ARBR", Value(row, "ARBR")),
            ("UGANKO", Value(row, "UGANKO")),
            ("PRINTPGM", Value(row, "PRINTPGM")),
            ("UGHOLD", Value(row, "UGHOLD")));
    }

    private static Dictionary<string, string> PositionKey(string documentNo, string article, string timestamp) =>
        Dict(
            ("SLNBNR", documentNo),
            ("PSIDNR", article),
            ("KEYTYPE", "LPSDA"),
            ("PSPOSI", "1"),
            ("PSBGZT", timestamp),
            ("PSKOPO", "0"),
            ("SLNANW", "LBS"),
            ("SLNPOS", "1"),
            ("PSBGNR", documentNo));

    private static string GetCurrentListSsid(IReadOnlyDictionary<string, string> headerDta) =>
        Get(headerDta, "SSID");

    private static void ValidateLabelLoad(IReadOnlyDictionary<string, string> dta, string documentNo, string timestamp)
    {
        if (!string.Equals(Get(dta, "LFNR"), documentNo, StringComparison.Ordinal)
            || !string.Equals(Get(dta, "BGZT"), timestamp, StringComparison.Ordinal)
            || !string.Equals(Get(dta, "POSI"), "1", StringComparison.Ordinal))
            throw new ProcessConflictException("EK99103R *LOAD hat nicht die erwartete Lagerbewegung geöffnet.");
    }

    private static void ValidateLabelMovementDetails(
        IReadOnlyDictionary<string, string> dta,
        TankOutRequest request,
        MovementRow movement,
        string documentNo)
    {
        if (!string.Equals(Get(dta, "PSBGNR"), documentNo, StringComparison.Ordinal)
            || !string.Equals(Get(dta, "PSPOSI"), "1", StringComparison.Ordinal)
            || !string.Equals(Get(dta, "PSBWKZ"), "LE", StringComparison.Ordinal)
            || !string.Equals(Get(dta, "PSIDNR"), request.Article, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Get(dta, "PSPONR"), request.Batch, StringComparison.Ordinal)
            || !string.Equals(Get(dta, "PSLAGO"), request.TargetWarehouse, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Get(dta, "PSLAPL"), request.TargetStorageBin, StringComparison.Ordinal)
            || !string.Equals(Get(dta, "PSBGZT"), movement.Timestamp, StringComparison.Ordinal)
            || Math.Abs(ParseQuantity(Get(dta, "PSBMN1")) - movement.Quantity) >= 0.0005m)
            throw new ProcessConflictException("LB31004R bestätigte nicht exakt die erwartete LE-Zielbewegung für den Etikettendruck.");
    }

    private static decimal ParseQuantity(string value)
    {
        var numeric = new string((value ?? "").Trim()
            .TakeWhile(ch => char.IsDigit(ch) || ch is '+' or '-' or ',' or '.').ToArray());
        if (string.IsNullOrWhiteSpace(numeric))
            throw new FormatException($"Oxaion quantity '{value}' does not start with a numeric value.");

        if (numeric.Contains(',') && numeric.Contains('.'))
            numeric = numeric.LastIndexOf(',') > numeric.LastIndexOf('.')
                ? numeric.Replace(".", "").Replace(',', '.')
                : numeric.Replace(",", "");
        else if (numeric.Contains(','))
            numeric = numeric.Replace(',', '.');

        return decimal.Parse(numeric, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    private static string Value(XElement row, string name) =>
        row.Element(name)?.Value.Trim()
        ?? row.Element("KEY")?.Element(name)?.Value.Trim()
        ?? "";

    private static string Get(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : "";

    private static Dictionary<string, string> Dict(params (string Key, string Value)[] values) =>
        values.ToDictionary(x => x.Key, x => x.Value ?? "", StringComparer.Ordinal);

    private static Dictionary<string, string> Merge(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        var result = new Dictionary<string, string>(left, StringComparer.Ordinal);
        foreach (var pair in right) result[pair.Key] = pair.Value ?? "";
        return result;
    }

    private static void Validate(TankOutLabelPrintRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClientOperationId)
            || string.IsNullOrWhiteSpace(request.TankOutOperationId)
            || string.IsNullOrWhiteSpace(request.PersonnelNo)
            || string.IsNullOrWhiteSpace(request.PersonnelName))
            throw new ArgumentException("Druckvorgang, Tank-Auslagerung und Mitarbeiter sind erforderlich.");
        if (request.LabelCount <= 0)
            throw new ArgumentException("Anzahl Etiketten muss eine positive ganze Zahl sein.");
    }

    private async Task SaveEventAsync(SeparateOperation tx, string stage, string message, CancellationToken ct)
    {
        tx.Stage = stage;
        tx.Message = message;
        tx.Events.Add(new TransactionEvent(DateTimeOffset.UtcNow, stage, message));
        await _store.SaveAsync(tx, ct);
    }
}
