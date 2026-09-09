using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace Fam.Pulverentnahme.Web;

public sealed partial class MaterialTransferBookingService
{
    private const int MaxTargetF4Pages = 100;

    private async Task ValidateLfDestinationAsync(OxaionSession session, Dictionary<string, string> positionState, TransferSpec spec, CancellationToken ct)
    {
        var warehouseF4 = Merge(positionState, Dict(
            ("PFIELD", "TX_LAGO2"), ("FIELD", "TX_LAG2"), ("MFLD", "TX_LAG2")));
        var warehouseLookup = await session.CallAsync("LB20115J", "*F4", warehouseF4, ct);
        OxaionSession.AssertNoFcod(warehouseLookup);
        var warehouseSsid = Get(warehouseLookup.Dta, "SSID");
        if (string.IsNullOrWhiteSpace(warehouseSsid) || !string.Equals(Get(warehouseLookup.Dta, "PGMN"), "US16601R", StringComparison.Ordinal))
            throw new InvalidOperationException("LB20115J target-warehouse F4 did not open the confirmed US16601R list.");

        OxaionSession.AssertNoFcod(await session.CallAsync("US16601R", "*GETHDR", Dict(
            ("FLD", "TX_LAG2"), ("CPY-FRSSID", ""), ("NOHWPgm", "US16601"),
            ("SSID", warehouseSsid), ("PFLD", "TX_LAGO2")), ct));
        var warehouseRows = await ReadCompleteF4RowsAsync(
            session,
            "US16601R",
            warehouseSsid,
            Dict(("FLD", "TX_LAG2"), ("SSID", warehouseSsid), ("PFLD", "TX_LAGO2"), ("mode", "replace")),
            ct);

        var warehouseMatches = warehouseRows.Where(row =>
            string.Equals(row.Element("KEY")?.Element("TX_LAG2")?.Value.Trim(), spec.ToWarehouse, StringComparison.OrdinalIgnoreCase)).ToList();
        if (warehouseMatches.Count != 1)
            throw new ProcessConflictException($"Ziel-Lagerort {spec.ToWarehouse} wurde in der bestätigten Oxaion-Lagerortliste nicht eindeutig gefunden.");
        var binManaged = string.Equals(warehouseMatches[0].Element("ULGSTP.LGKLPL")?.Value.Trim(), "J", StringComparison.OrdinalIgnoreCase);
        if (binManaged && string.IsNullOrWhiteSpace(spec.ToStorageBin))
            throw new ProcessConflictException($"Ziel-Lagerort {spec.ToWarehouse} ist lagerplatzgeführt. Ein Oxaion-Lagerplatz ist erforderlich.");
        if (!binManaged && !string.IsNullOrWhiteSpace(spec.ToStorageBin))
            throw new ProcessConflictException($"Ziel-Lagerort {spec.ToWarehouse} ist laut Oxaion nicht lagerplatzgeführt. Es darf kein Lagerplatz übergeben werden.");
        if (!binManaged) return;

        var binF4 = Merge(positionState, Dict(
            ("PFIELD", "*NONE *NONE *NONE TX_LAG2 TX_LAP2"),
            ("FIELD", "*NONE *NONE *NONE *NONE *NONE"),
            ("MFLD", "TX_LAP2"), ("TX_LAG2", spec.ToWarehouse), ("TX_LAP2", spec.ToStorageBin)));
        var binLookup = await session.CallAsync("LB20115J", "*F4", binF4, ct);
        OxaionSession.AssertNoFcod(binLookup);
        var binSsid = Get(binLookup.Dta, "SSID");
        if (string.IsNullOrWhiteSpace(binSsid) || !string.Equals(Get(binLookup.Dta, "PGMN"), "LB13210R", StringComparison.Ordinal))
            throw new InvalidOperationException("LB20115J target-bin F4 did not open the confirmed LB13210R list.");

        const string noFields = "*NONE *NONE *NONE *NONE *NONE";
        const string parentFields = "*NONE *NONE *NONE TX_LAG2 TX_LAP2";
        OxaionSession.AssertNoFcod(await session.CallAsync("LB13210R", "*GETHDR", Dict(
            ("FLD", noFields), ("CPY-FRSSID", ""), ("NOHWPgm", "LB13210"),
            ("SSID", binSsid), ("PFLD", parentFields)), ct));
        var binRows = await ReadCompleteF4RowsAsync(
            session,
            "LB13210R",
            binSsid,
            Dict(("FLD", noFields), ("SSID", binSsid), ("PFLD", parentFields), ("mode", "replace")),
            ct);

        var binMatches = binRows.Count(row =>
            string.Equals(row.Element("KEY")?.Element("TX_LAG2")?.Value.Trim(), spec.ToWarehouse, StringComparison.OrdinalIgnoreCase)
            && string.Equals(row.Element("KEY")?.Element("TX_LAP2")?.Value.Trim(), spec.ToStorageBin, StringComparison.Ordinal));
        if (binMatches != 1)
            throw new ProcessConflictException($"Ziel-Lagerplatz {spec.ToWarehouse} / {spec.ToStorageBin} wurde in der bestätigten Oxaion-Lagerplatzliste nicht eindeutig gefunden.");
    }

    private static async Task<IReadOnlyList<XElement>> ReadCompleteF4RowsAsync(
        OxaionSession session,
        string program,
        string ssid,
        IReadOnlyDictionary<string, string> firstListContext,
        CancellationToken ct)
    {
        var rows = new List<XElement>();
        var seenNonTerminalPages = new HashSet<string>(StringComparer.Ordinal);
        var page = await session.CallAsync(program, "*FIRSTLIST", new Dictionary<string, string>(firstListContext, StringComparer.Ordinal), ct);
        OxaionSession.AssertNoFcod(page);

        for (var pageNumber = 1; pageNumber <= MaxTargetF4Pages; pageNumber++)
        {
            var pageRows = page.Xml.Descendants("ROW").ToList();
            var terminal = MachineStockService.HasStop(page.Xml);
            if (!terminal)
            {
                if (pageRows.Count == 0)
                    throw new InvalidOperationException($"{program} target-validation list returned an empty non-terminal page.");

                var signature = string.Join("\n", pageRows.Select(row => row.ToString(SaveOptions.DisableFormatting)));
                if (!seenNonTerminalPages.Add(signature))
                    throw new InvalidOperationException($"{program} target-validation pagination did not advance; repeated page detected.");
            }

            rows.AddRange(pageRows.Select(row => new XElement(row)));
            if (terminal) return rows;

            // Oxaion R-list pagination with *NEXTLIST + SSID is already confirmed in the project
            // for paged list programs. Keep the same bounded, fail-closed mechanism here; a target
            // key is accepted only after a later page returns STOP and the exact key is unique.
            page = await session.CallAsync(program, "*NEXTLIST", Dict(("SSID", ssid)), ct);
            OxaionSession.AssertNoFcod(page);
        }

        throw new InvalidOperationException($"{program} target-validation list did not return STOP within {MaxTargetF4Pages} pages.");
    }

    private async Task<Dictionary<string, string>> NewHeaderAsync(OxaionSession session, CancellationToken ct)
    {
        var load = await session.CallAsync("LB20100J", "*LOADNEW", Dict(
            ("ISSID", "HTTPWEB" + Guid.NewGuid().ToString("N")),
            ("NOHWPgm", "LB20100"), ("SSID", ""), ("KOBGNR", ""), ("KEYTYPE", "C_LKOPF")), ct);
        OxaionSession.AssertNoFcod(load);
        var created = await session.CallAsync("LB20100J", "*NEW", Merge(load.Dta, Dict(("KEYTYPE", "C_LKOPF"))), ct);
        OxaionSession.AssertNoFcod(created);
        return created.Dta;
    }

    private async Task<Dictionary<string, string>> AddPositionAsync(
        OxaionSession session,
        SeparateOperation tx,
        string ssid,
        Dictionary<string, string> previous,
        DateOnly bookingDate,
        (string Operator, string BookingText) op,
        TransferSpec spec,
        CancellationToken ct)
    {
        var position = spec.Position.ToString(CultureInfo.InvariantCulture);
        await SaveEventAsync(tx, $"POSITION_{position}_VALIDATING", $"Validating {spec.BookingKey} position {position}.", ct);
        var seed = Merge(previous, Dict(
            ("SSID", ssid), ("SNR", position), ("WSTR", "1"), ("PGMN", "LB20110R"), ("MTYPE", "*PGM"),
            ("NAME", "UPOSTP.POPONR"), ("MC-Modus", "true"), ("NoModDlg", "true"),
            ("keyFields", "PSBGNR PSPOSI PSKOPO PSBGZT"), ("keyFirm", "FIRM"), ("KEYTYPE", "C_LKOPF")));
        var created = await session.CallAsync("LB20115J", "*NEW", seed, ct);
        OxaionSession.AssertNoFcod(created);
        var state = Merge(seed, created.Dta);

        var first = Merge(state, PositionFields(tx, bookingDate, op, spec, "0,000", FormatQty(spec.QuantityKg), "J"));
        var r1 = await session.CallAsync("LB20115J", "*PUTNEW", first, ct);
        OxaionSession.AssertNoFcod(r1);
        state = Merge(first, r1.Dta);

        // The tank-out trace confirms the target warehouse/bin F4 lists on the prepared LF
        // position. Validate the exact Oxaion keys before LB20110R *UPD; never trust free text
        // as an ERP key. This validation is only required for LF (tank -> warehouse).
        if (string.Equals(spec.BookingKey, "LF", StringComparison.Ordinal))
            await ValidateLfDestinationAsync(session, state, spec, ct);

        var tcode = FirstText(r1.Xml, "TCODE");
        var q1 = FirstText(r1.Xml, "PSBMN1");
        var q2 = FirstText(r1.Xml, "PSBMN2");
        var newTs = FirstText(r1.Xml, "PSBGZT");
        Dictionary<string, string> validated;
        if (tcode == "WIN3")
        {
            var win = await session.CallAsync("LB20115J", "*LOADWIN3", Dict(("NOHWPgm", "LB201153"), ("NoHints", "")), ct);
            OxaionSession.AssertNoFcod(win);
            state = Merge(state, win.Dta);
            var final = Merge(state, PositionFields(tx, bookingDate, op, spec, FormatQty(spec.QuantityKg), FormatQty(spec.QuantityKg), "N"));
            final["NoVPDialog"] = "true";
            final["NoWinSnnr"] = "true";
            final["NoWindow"] = "true";
            var fr = await session.CallAsync("LB20115J", "*PUTNEW", final, ct);
            OxaionSession.AssertNoFcod(fr);
            validated = Merge(final, fr.Dta);
        }
        else if (!string.IsNullOrWhiteSpace(newTs)
                 && newTs != Get(first, "PSBGZT")
                 && !string.IsNullOrWhiteSpace(q1)
                 && !string.IsNullOrWhiteSpace(q2))
        {
            validated = state;
        }
        else
        {
            throw new InvalidOperationException($"Position {position} PUTNEW returned neither TCODE=WIN3 nor the proven final HTTP state.");
        }

        tx.Status = TransactionStatuses.SendingToOxaion;
        await SaveEventAsync(tx, $"POSITION_{position}_UPD_SENT", $"Submitting LB20110R *UPD for position {position}.", ct);
        var update = await session.CallAsync("LB20110R", "*UPD", Merge(validated, Dict(("SSID", ssid), ("mode", "update"), ("KEYTYPE", "C_LKOPF"))), ct);
        OxaionSession.AssertNoFcod(update);
        var rows = MixBookingService.ParseMovements(update.Xml);
        var pairSpecs = new[] { spec };
        if (!MovementsComplete(pairSpecs, rows.Where(r => r.Position == position).ToList(), out var message))
            throw new InvalidOperationException($"LB20110R *UPD did not return the expected pair for position {position}: {message}");
        await SaveEventAsync(tx, $"POSITION_{position}_CONFIRMED", $"Position {position} confirmed by Oxaion.", ct);
        return validated;
    }

    private Dictionary<string, string> PositionFields(
        SeparateOperation tx,
        DateOnly bookingDate,
        (string Operator, string BookingText) op,
        TransferSpec spec,
        string q1,
        string q2,
        string first)
    {
        var fields = Dict(
            ("PSANWG", "LBS"), ("PSBGKZ", "MB"), ("PSBGNR", tx.DocumentNo!), ("PSBGDT", Iso(bookingDate)),
            ("PSBGTX", op.BookingText), ("TX_BGT1", op.Operator), ("PSFIRM", _options.Firm),
            ("PSPOSI", spec.Position.ToString(CultureInfo.InvariantCulture)), ("PSBWKZ", spec.BookingKey),
            ("PSIDNR", spec.Article), ("I_PSIDNR", spec.Article), ("DEMO_IDNR", spec.Article), ("POIDNR", spec.Article),
            ("I_TX_IDN2", ""), ("I_TX_PCKMM", ""), ("I_TX_PCKMS", ""), ("I_TX_PCKMZ", ""),
            ("PSLAGO", spec.FromWarehouse), ("PSPONR", spec.FromBatch), ("PSLAPL", spec.FromStorageBin ?? ""),
            ("TX_LAG2", spec.ToWarehouse), ("TX_PON2", spec.ToBatch ?? ""),
            ("PSBMN1", q1), ("PSBMN2", q2), ("TX_FIRST", first),
            ("TX_LAGO", TextOrCode(spec.FromWarehouseText, spec.FromWarehouse)),
            ("TX_LAGO2", TextOrCode(spec.ToWarehouseText, spec.ToWarehouse)), ("TX_PDBZ2", "pro 1"),
            ("KEYTYPE", "C_LKOPF"), ("mode", "merge"));
        if (!string.IsNullOrWhiteSpace(spec.ToStorageBin)) fields["TX_LAP2"] = spec.ToStorageBin;
        if (spec.ProductionDate is not null) fields["PSPRDT"] = Iso(spec.ProductionDate.Value);
        return fields;
    }

    private static Dictionary<string, string> TargetAccessStateFromRows(TransferSpec spec, XDocument xml, Dictionary<string, string> fallback)
    {
        var expectedPosition = spec.Position.ToString(CultureInfo.InvariantCulture);
        var accessKey = spec.BookingKey == "LM" ? "LN" : spec.BookingKey == "LF" ? "LE" : "";
        var expectedBatch = spec.BookingKey == "LM" ? spec.ToBatch : spec.FromBatch;
        foreach (var row in xml.Descendants("ROW"))
        {
            if (row.Element("LPSDAP.PSBWKZ")?.Value.Trim() != accessKey
                || row.Element("LPSDAP.PSPONR")?.Value.Trim() != expectedBatch)
                continue;
            var key = row.Element("KEY");
            var pos = row.Element("LPSDAP.PSPOSI")?.Value.Trim();
            if (string.IsNullOrWhiteSpace(pos)) pos = key?.Element("PSPOSI")?.Value.Trim();
            if (pos != expectedPosition) continue;
            var ts = key?.Element("PSBGZT")?.Value.Trim();
            if (string.IsNullOrWhiteSpace(ts)) continue;
            var state = new Dictionary<string, string>(fallback, StringComparer.Ordinal)
            {
                ["PSBGZT"] = ts,
                ["PSPOSI"] = expectedPosition
            };
            var kopo = key?.Element("PSKOPO")?.Value.Trim();
            if (!string.IsNullOrWhiteSpace(kopo)) state["PSKOPO"] = kopo;
            return state;
        }
        throw new InvalidOperationException($"Persisted access row for position {spec.Position} was not found.");
    }

    private static IReadOnlyList<ExpectedTransferMovement> BuildExpected(IReadOnlyList<TransferSpec> specs)
    {
        var expected = new List<ExpectedTransferMovement>(specs.Count * 2);
        foreach (var s in specs)
        {
            var p = s.Position.ToString(CultureInfo.InvariantCulture);
            expected.Add(new ExpectedTransferMovement(p, s.BookingKey, s.Article, s.FromBatch, s.FromWarehouse, s.FromStorageBin ?? "", s.QuantityKg));
            if (s.BookingKey == "LM")
                expected.Add(new ExpectedTransferMovement(p, "LN", s.Article, s.ToBatch, s.ToWarehouse, s.ToStorageBin ?? "", s.QuantityKg));
            else if (s.BookingKey == "LF")
                expected.Add(new ExpectedTransferMovement(p, "LE", s.Article, s.FromBatch, s.ToWarehouse, s.ToStorageBin ?? "", s.QuantityKg));
            else
                throw new InvalidOperationException("Unsupported transfer booking key " + s.BookingKey);
        }
        return expected;
    }

    private async Task<DocumentContext> OpenExistingDocumentAsync(OxaionSession session, SeparateOperation tx, CancellationToken ct)
    {
        var open = await session.CallAsync("LB20100J", "*OPEN", Dict(("KOBGNR", tx.DocumentNo!), ("KEYTYPE", "LKOPF"), ("noAutCheck", "")), ct);
        OxaionSession.AssertNoFcod(open);
        var shortResult = await session.CallAsync("LB20090J", "*SHORT", Merge(tx.HeaderDta!, Dict(
            ("PSANWG", "LBS"), ("PSBGNR", tx.DocumentNo!), ("KEYTYPE", "C_LKOPF"))), ct);
        OxaionSession.AssertNoFcod(shortResult);
        var ssid = Get(shortResult.Dta, "SSID");
        if (string.IsNullOrWhiteSpace(ssid)) throw new InvalidOperationException("LB20090J *SHORT did not return SSID.");
        OxaionSession.AssertNoFcod(await session.CallAsync("LB20110R", "*GETHDR", Dict(("SSID", ssid)), ct));
        var list = await ReadDocumentListAsync(session, ssid, ct);
        return new DocumentContext(ssid, list);
    }

    private static async Task<OxaionCallResult> ReadDocumentListAsync(OxaionSession session, string ssid, CancellationToken ct)
    {
        var list = await session.CallAsync("LB20110R", "*FIRSTLIST", Dict(("FLD", ""), ("PFLD", ""), ("SSID", ssid), ("mode", "replace")), ct);
        OxaionSession.AssertNoFcod(list);
        if (!MachineStockService.HasStop(list.Xml))
            throw new InvalidOperationException("LB20110R material-document list did not return STOP; incomplete verification is not accepted.");
        return list;
    }

    private static (string Operator, string BookingText) OperatorContext(
        SeparateOperation tx, string personnelNo, string personnelName, string bookingText)
    {
        var operatorText = string.IsNullOrWhiteSpace(personnelName) ? $"PN {personnelNo}" : $"PN {personnelNo} | {personnelName}";
        var prefix = $"{tx.TransactionId[..12]}|PN{personnelNo}";
        var room = Math.Max(0, 50 - prefix.Length - 1);
        var text = room > 0 && !string.IsNullOrWhiteSpace(bookingText)
            ? prefix + "|" + bookingText[..Math.Min(room, bookingText.Length)]
            : prefix;
        return (operatorText, text);
    }

    private async Task SaveEventAsync(SeparateOperation tx, string stage, string message, CancellationToken ct)
    {
        tx.Stage = stage;
        tx.Message = message;
        tx.Events.Add(new TransactionEvent(DateTimeOffset.UtcNow, stage, message));
        await _store.SaveAsync(tx, ct);
    }

    private sealed record DocumentContext(string Ssid, OxaionCallResult List);
    private static Dictionary<string, string> Dict(params (string Key, string Value)[] values) => values.ToDictionary(x => x.Key, x => x.Value ?? "", StringComparer.Ordinal);
    private static Dictionary<string, string> Merge(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
    {
        var r = new Dictionary<string, string>(a, StringComparer.Ordinal);
        foreach (var item in b) r[item.Key] = item.Value ?? "";
        return r;
    }
    private static string Get(IReadOnlyDictionary<string, string> values, string key) => values.TryGetValue(key, out var v) ? v : "";
    private static string FirstText(XDocument xml, string name) => xml.Descendants(name).FirstOrDefault()?.Value.Trim() ?? "";
    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string FormatQty(decimal v) => v.ToString("0.000", CultureInfo.GetCultureInfo("de-AT"));
    private static string TextOrCode(string text, string code) => string.IsNullOrWhiteSpace(text) ? code : text;
}
