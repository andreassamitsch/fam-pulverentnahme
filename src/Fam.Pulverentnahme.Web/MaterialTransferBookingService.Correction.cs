using System.Globalization;
using System.Xml.Linq;

namespace Fam.Pulverentnahme.Web;

public sealed partial class MaterialTransferBookingService
{
    internal const string FamCorrectionBusinessArea = "21";
    internal const string FamCorrectionCostCenter = "5100";
    internal const string FamCorrectionCostCenterText = "3D-Druck";
    internal const string CorrectionDialogKeyType = "LKOPF";

    internal static string ExpectedCorrectionStockDirection(string bookingKey) => bookingKey switch
    {
        "I1" => "1",
        "I2" => "2",
        _ => throw new ArgumentOutOfRangeException(nameof(bookingKey), bookingKey, "Only I1/I2 correction directions are confirmed.")
    };

    public async Task BookInventoryCorrectionAsync(
        SeparateOperation tx,
        DateOnly bookingDate,
        string personnelNo,
        string personnelName,
        string bookingText,
        string bookingKey,
        string article,
        string articleText,
        string warehouse,
        string warehouseText,
        string batch,
        decimal quantityKg,
        CancellationToken ct)
    {
        if (bookingKey is not ("I1" or "I2"))
            throw new ArgumentException("Only I1/I2 inventory corrections are confirmed.");
        if (quantityKg <= 0m)
            throw new ArgumentException("Correction quantity must be > 0.");

        await using var session = await _oxaion.ConnectAsync(ct);
        try
        {
            await ValidateCorrectionBookingKeyAsync(session, bookingKey, ct);

            var header = await NewHeaderAsync(session, ct);
            await SaveEventAsync(tx, "CORRECTION_HEADER_SUBMITTING", "Creating Oxaion material document for tank correction.", ct);
            var op = OperatorContext(tx, personnelNo, personnelName, bookingText);
            var put = await session.CallAsync("LB20100J", "*PUTNEW", Merge(header, Dict(
                ("KOBGDT", Iso(bookingDate)),
                ("KOBGT1", op.Operator),
                ("KOBGTX", op.BookingText),
                ("KOBGKZ", "MB"),
                ("KOFIRM", _options.Firm),
                ("KEYTYPE", "C_LKOPF"))), ct);
            OxaionSession.AssertNoFcod(put);

            var doc = Get(put.Dta, "KOBGNR");
            if (string.IsNullOrWhiteSpace(doc))
                throw new InvalidOperationException("LB20100J *PUTNEW did not return KOBGNR for inventory correction.");

            tx.DocumentNo = doc;
            tx.HeaderDta = put.Dta;
            tx.Status = TransactionStatuses.SendingToOxaion;
            await SaveEventAsync(tx, "CORRECTION_HEADER_CREATED", $"Oxaion correction document {doc} created.", ct);

            var context = await OpenExistingDocumentAsync(session, tx, ct);
            await AddFirstCorrectionPositionAsync(
                session, tx, context.Ssid, bookingDate, op, bookingKey,
                article, articleText, warehouse, warehouseText, batch, quantityKg, ct);

            await SaveEventAsync(tx, "CORRECTION_ENDING", "Closing Oxaion correction document.", ct);
            OxaionSession.AssertNoFcod(await session.CallAsync(
                "LB20100J", "*END", Dict(("KOBGNR", tx.DocumentNo), ("KEYTYPE", "LKOPF")), ct));

            await VerifyAndCloseCorrectionAsync(
                session, tx, bookingKey, article, warehouse, batch, quantityKg, ct);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tx.DocumentNo))
            {
                try
                {
                    await session.CallAsync(
                        "LB20100J", "*END",
                        Dict(("KOBGNR", tx.DocumentNo!), ("KEYTYPE", "LKOPF")),
                        CancellationToken.None);
                }
                catch
                {
                    // Best-effort close only. Never turn cleanup into a blind rebooking.
                }
            }
        }
    }

    public async Task<bool> ReconcileInventoryCorrectionAsync(
        SeparateOperation tx,
        string bookingKey,
        string article,
        string warehouse,
        string batch,
        decimal quantityKg,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tx.DocumentNo) || tx.HeaderDta is null)
            return false;

        await using var session = await _oxaion.ConnectAsync(ct);
        try
        {
            var context = await OpenExistingDocumentAsync(session, tx, ct);
            var rows = MixBookingService.ParseMovements(context.List.Xml);
            tx.LastMovements = rows.ToList();
            if (!CorrectionMovementComplete(rows, bookingKey, article, warehouse, batch, quantityKg, out var message))
            {
                tx.Status = TransactionStatuses.ManualReviewRequired;
                await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED", message, ct);
                try
                {
                    OxaionSession.AssertNoFcod(await session.CallAsync(
                        "LB20100J", "*END",
                        Dict(("KOBGNR", tx.DocumentNo!), ("KEYTYPE", "LKOPF")),
                        CancellationToken.None));
                }
                catch { }
                return false;
            }

            OxaionSession.AssertNoFcod(await session.CallAsync(
                "LB20100J", "*END",
                Dict(("KOBGNR", tx.DocumentNo!), ("KEYTYPE", "LKOPF")),
                CancellationToken.None));
            tx.Status = TransactionStatuses.Success;
            await SaveEventAsync(tx, "SUCCESS",
                $"Oxaion correction document {tx.DocumentNo} contains exactly the expected {bookingKey} movement and was explicitly closed.",
                CancellationToken.None);
            return true;
        }
        catch (OxaionTransportException ex)
        {
            tx.Status = TransactionStatuses.Uncertain;
            await SaveEventAsync(tx, "UNCERTAIN", ex.Message, ct);
            return false;
        }
        catch (Exception ex)
        {
            tx.Status = TransactionStatuses.ManualReviewRequired;
            await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED", ex.Message, ct);
            return false;
        }
    }

    internal static bool CorrectionMovementComplete(
        IReadOnlyList<MovementRow> rows,
        string bookingKey,
        string article,
        string warehouse,
        string batch,
        decimal quantityKg,
        out string message)
    {
        if (rows.Count != 1)
        {
            message = $"Expected exactly one {bookingKey} correction movement, found {rows.Count}. No automatic retry is allowed.";
            return false;
        }

        var row = rows[0];
        var ok = row.Position == "1"
            && row.BookingKey == bookingKey
            && row.Article == article
            && row.Warehouse == warehouse
            && row.Batch == batch
            && string.IsNullOrWhiteSpace(row.StorageBin)
            && Math.Abs(row.Quantity - quantityKg) < 0.0005m;

        message = ok
            ? $"Expected {bookingKey} correction movement is present exactly once."
            : $"Correction movement does not match expected {bookingKey}/{warehouse}/{batch}/{quantityKg:0.###} kg.";
        return ok;
    }

    private async Task ValidateCorrectionBookingKeyAsync(OxaionSession session, string bookingKey, CancellationToken ct)
    {
        var read = await session.CallAsync("US50000J", "*READ", Dict(
            ("LBBWKZ", bookingKey),
            ("LBFIRM", "")), ct);
        OxaionSession.AssertNoFcod(read);

        var returnedKey = Get(read.Dta, "LBBWKZ");
        var description = Get(read.Dta, "LBBWBZ");
        var lagerBookingAllowed = Get(read.Dta, "LBKLAS");
        if (!CorrectionBookingKeyMatches(bookingKey, returnedKey, description, lagerBookingAllowed))
            throw new ProcessConflictException(
                $"Oxaion-Buchungsschlüssel {bookingKey} entspricht nicht der bestätigten FAM-Konfiguration. " +
                $"Aktuell: '{returnedKey}' / '{description}', LBKLAS='{lagerBookingAllowed}'. Es wurde keine Korrektur gebucht.");

    }

    internal static bool CorrectionBookingKeyMatches(
        string bookingKey,
        string returnedKey,
        string description,
        string lagerBookingAllowed)
    {
        var expectedDescription = bookingKey == "I2"
            ? "Bestandskorr. Abgang (Schwund)"
            : "Bestandskorrektur Zugang";

        return bookingKey is "I1" or "I2"
            && string.Equals(returnedKey, bookingKey, StringComparison.Ordinal)
            && string.Equals(description, expectedDescription, StringComparison.Ordinal)
            && string.Equals(lagerBookingAllowed, "J", StringComparison.OrdinalIgnoreCase);
    }

    private async Task AddFirstCorrectionPositionAsync(
        OxaionSession session,
        SeparateOperation tx,
        string ssid,
        DateOnly bookingDate,
        (string Operator, string BookingText) op,
        string bookingKey,
        string article,
        string articleText,
        string warehouse,
        string warehouseText,
        string batch,
        decimal quantityKg,
        CancellationToken ct)
    {
        await SaveEventAsync(tx, "CORRECTION_POSITION_VALIDATING",
            $"Validating confirmed {bookingKey} correction on {warehouse}/{batch}.", ct);

        var load = await session.CallAsync("LB20115J", "*LOAD", Dict(
            ("NOHWPgm", "LB20115"),
            ("SSID", ssid),
            ("mode", "merge")), ct);
        OxaionSession.AssertNoFcod(load);

        var seed = Merge(load.Dta, tx.HeaderDta!);
        seed = Merge(seed, Dict(
            ("ANWG", "LBS"),
            ("PSANWG", "LBS"),
            ("PSBGNR", tx.DocumentNo!),
            ("PSBGDT", Iso(bookingDate)),
            ("PSBGKZ", "MB"),
            ("PSFIRM", _options.Firm),
            ("PSPOSI", "1"),
            ("PSBMN1", "0,000"),
            ("PSBMN2", "0,000"),
            ("TX_B1SB01", ExpectedCorrectionStockDirection(bookingKey)),
            ("TX_FIRST", "J"),
            ("KEYTYPE", CorrectionDialogKeyType),
            ("SSID", ssid),
            ("SNR", "1"),
            ("WSTR", "1"),
            ("PGMN", "LB20110R"),
            ("MTYPE", "*PGM"),
            ("NAME", "UPOSTP.POPONR"),
            ("MC-Modus", "true"),
            ("NoModDlg", "true"),
            ("keyFields", "PSBGNR PSPOSI PSKOPO PSBGZT"),
            ("keyFirm", "FIRM")));

        var created = await session.CallAsync("LB20115J", "*NEW", seed, ct);
        OxaionSession.AssertNoFcod(created);
        var state = Merge(seed, created.Dta);
        var amount = FormatQty(quantityKg);
        var expectedDirection = ExpectedCorrectionStockDirection(bookingKey);

        // Do not jump directly to the final-looking form payload. The fresh successful
        // 2026-09-29 JET traces prove that LB20115J first runs the field-specific F4/plain
        // lookups. Besides validating the real keys, those calls establish the same dialog
        // state Oxaion uses before entryChkIDNR04. TX_* description fields are taken from
        // those Oxaion responses rather than invented by the WebApp.
        state = await PrepareCorrectionDialogStateAsync(
            session, tx, state, bookingKey, article, warehouse, batch, amount, ct);

        var firstPass = Merge(state, CorrectionFields(
            tx, bookingDate, op, bookingKey, article, articleText,
            warehouse, warehouseText, batch,
            "0,000", amount, "J"));

        var firstResult = await session.CallAsync("LB20115J", "*PUTNEW", firstPass, ct);
        var firstFcod = FirstText(firstResult.Xml, "FCOD");
        if (!string.IsNullOrWhiteSpace(firstFcod))
            throw new InvalidOperationException(
                $"Fresh confirmed {bookingKey} trace expects direct TCODE=WIN2, but Oxaion returned FCOD={firstFcod}. No LB20110R *UPD was sent.");

        var firstTcode = FirstText(firstResult.Xml, "TCODE");
        if (!string.Equals(firstTcode, "WIN2", StringComparison.Ordinal))
        {
            OxaionSession.AssertNoFcod(firstResult);
            throw new InvalidOperationException(
                $"{bookingKey} correction did not reach the freshly confirmed TCODE=WIN2 state. No LB20110R *UPD was sent.");
        }

        var returnedDirection = Get(firstResult.Dta, "TX_B1SB01");
        if (!string.Equals(returnedDirection, expectedDirection, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"{bookingKey} reached TCODE=WIN2, but Oxaion returned TX_B1SB01='{returnedDirection}' instead of '{expectedDirection}'. No LB20110R *UPD was sent.");

        state = Merge(firstPass, firstResult.Dta);

        var win = await session.CallAsync("LB20115J", "*LOADWIN2", Dict(
            ("NOHWPgm", "LB201152"),
            ("NoHints", "")), ct);
        OxaionSession.AssertNoFcod(win);
        state = Merge(state, win.Dta);

        var final = Merge(state, CorrectionFields(
            tx, bookingDate, op, bookingKey, article, articleText,
            warehouse, warehouseText, batch, amount, amount, "N"));
        final["NoVPDialog"] = "true";
        final["NoWinSnnr"] = "true";
        final["NoWindow"] = "true";

        var finalResult = await session.CallAsync("LB20115J", "*PUTNEW", final, ct);
        OxaionSession.AssertNoFcod(finalResult);
        var persistedState = Merge(final, finalResult.Dta);

        tx.Status = TransactionStatuses.SendingToOxaion;
        await SaveEventAsync(tx, "CORRECTION_POSITION_UPD_SENT",
            $"Submitting LB20110R *UPD for {bookingKey} correction.", ct);
        var update = await session.CallAsync("LB20110R", "*UPD", Merge(persistedState, Dict(
            ("SSID", ssid),
            ("mode", "update"),
            ("KEYTYPE", CorrectionDialogKeyType))), ct);
        OxaionSession.AssertNoFcod(update);

        var rows = MixBookingService.ParseMovements(update.Xml)
            .Where(r => r.Position == "1")
            .ToList();
        if (!CorrectionMovementComplete(rows, bookingKey, article, warehouse, batch, quantityKg, out var message))
            throw new InvalidOperationException("Correction movement was not confirmed exactly: " + message);

        await SaveEventAsync(tx, "CORRECTION_POSITION_CONFIRMED",
            $"{bookingKey} correction position confirmed exactly by Oxaion.", ct);
    }

    private async Task<Dictionary<string, string>> PrepareCorrectionDialogStateAsync(
        OxaionSession session,
        SeparateOperation tx,
        Dictionary<string, string> state,
        string bookingKey,
        string article,
        string warehouse,
        string batch,
        string amount,
        CancellationToken ct)
    {
        state["TX_B1SB01"] = ExpectedCorrectionStockDirection(bookingKey);
        state["TX_FIRST"] = "J";
        state["PSPOSI"] = "1";
        state["I_PSIDNR"] = article;

        // 1) Booking key: LB20115J *F4 -> US50002R.
        var bookingKeyF4 = Merge(state, Dict(
            ("PSBWKZ", bookingKey),
            ("MFLD", "PSBWKZ"),
            ("PFIELD", "TX_BWKZ"),
            ("FIELD", "*NONE PSBWKZ")));
        var bookingRows = await ReadCorrectionF4RowsAsync(
            session,
            bookingKeyF4,
            "US50002R",
            "*NONE PSBWKZ",
            "TX_BWKZ",
            "PSBWKZ",
            bookingKey,
            includeNewActg: false,
            ct);
        var bookingMatch = RequireSingleCorrectionRow(
            bookingRows,
            row => Key(row, "PSBWKZ").Equals(bookingKey, StringComparison.Ordinal),
            $"Buchungsschlüssel {bookingKey}");
        state["PSBWKZ"] = bookingKey;
        state["TX_BWKZ"] = Key(bookingMatch, "TX_BWKZ");
        await SaveEventAsync(tx, "CORRECTION_DIALOG_BOOKING_KEY_VALIDATED",
            $"{bookingKey} was resolved through the confirmed LB20115J/US50002R F4 path.", ct);

        // 2) Warehouse: LB20115J *F4 -> US16601R.
        state["PSBMN2"] = amount;
        var warehouseF4 = Merge(state, Dict(
            ("PSLAGO", warehouse),
            ("MFLD", "PSLAGO"),
            ("PFIELD", "TX_LAGO"),
            ("FIELD", "PSLAGO")));
        var warehouseRows = await ReadCorrectionF4RowsAsync(
            session,
            warehouseF4,
            "US16601R",
            "PSLAGO",
            "TX_LAGO",
            "PSLAGO",
            warehouse,
            includeNewActg: false,
            ct);
        var warehouseMatch = RequireSingleCorrectionRow(
            warehouseRows,
            row => Key(row, "PSLAGO").Equals(warehouse, StringComparison.OrdinalIgnoreCase),
            $"Lagerort {warehouse}");
        state["PSLAGO"] = warehouse;
        state["TX_LAGO"] = Key(warehouseMatch, "TX_LAGO");
        await SaveEventAsync(tx, "CORRECTION_DIALOG_WAREHOUSE_VALIDATED",
            $"Warehouse {warehouse} was resolved through the confirmed LB20115J/US16601R F4 path.", ct);

        // 3) Batch/article: LB20115J *F4 -> US17402R. The UI opens the batch list and
        // selects the exact batch/article pair; it does not type a display text into TX_*.
        var batchF4 = Merge(state, Dict(
            ("PSPONR", ""),
            ("MFLD", "PSPONR"),
            ("FIELD", "PSPONR PSIDNR")));
        var batchRows = await ReadCorrectionF4RowsAsync(
            session,
            batchF4,
            "US17402R",
            "PSPONR PSIDNR",
            "",
            "PSPONR",
            "",
            includeNewActg: true,
            ct);
        var batchMatch = RequireSingleCorrectionRow(
            batchRows,
            row => Key(row, "PSPONR").Equals(batch, StringComparison.Ordinal)
                && Key(row, "PSIDNR").Equals(article, StringComparison.OrdinalIgnoreCase),
            $"Charge {batch} / Artikel {article}");
        state["PSPONR"] = batch;
        state["PSIDNR"] = article;
        state["I_PSIDNR"] = article;
        state["POIDNR"] = Key(batchMatch, "POIDNR");
        if (string.IsNullOrWhiteSpace(state["POIDNR"])) state["POIDNR"] = article;
        await SaveEventAsync(tx, "CORRECTION_DIALOG_BATCH_VALIDATED",
            $"Batch {batch} / article {article} was resolved through the confirmed LB20115J/US17402R F4 path.", ct);

        // 4) Article display text comes from Oxaion's confirmed GETPLAIN path.
        var articlePlain = await session.CallAsync("US00006J", "*GETPLAIN", Dict(
            ("I_PSIDNR", article),
            ("MFLD", "PSIDNR"),
            ("PGMN", "LB20115J"),
            ("PSIDNR", article),
            ("PFIELD", "TX_IDNR"),
            ("FIELD", "PSIDNR")), ct);
        OxaionSession.AssertNoFcod(articlePlain);
        var articleText = Get(articlePlain.Dta, "TX_IDNR");
        if (string.IsNullOrWhiteSpace(articleText))
            throw new ProcessConflictException(
                $"Oxaion lieferte für Artikel {article} über den bestätigten GETPLAIN-Weg keine Bezeichnung.");
        state["TX_IDNR"] = articleText;
        await SaveEventAsync(tx, "CORRECTION_DIALOG_ARTICLE_RESOLVED",
            $"Article {article} display text was resolved by Oxaion.", ct);

        // 5) Fixed FAM accounting keys are 21 / 5100, but the display description is still
        // resolved from the real Oxaion cost-center list. This validates the pair and avoids
        // hard-coding TX_KSTL.
        OxaionSession.AssertNoFcod(await session.CallAsync("US00006J", "*GETPLAIN", Dict(
            ("MFLD", "PSKSTL"),
            ("PGMN", "LB20115J"),
            ("PSWERK", FamCorrectionBusinessArea),
            ("PFIELD", "TX_KSTL"),
            ("FIELD", "*NONE PSWERK PSKSTL"),
            ("PSKSTL", "")), ct));

        state["PSWERK"] = FamCorrectionBusinessArea;
        state["PSKSTL"] = FamCorrectionCostCenter;
        var costCenterF4 = Merge(state, Dict(
            ("MFLD", "PSKSTL"),
            ("PFIELD", "TX_KSTL"),
            ("FIELD", "*NONE PSWERK PSKSTL")));
        var costCenterRows = await ReadCorrectionF4RowsAsync(
            session,
            costCenterF4,
            "US11001R",
            "*NONE PSWERK PSKSTL",
            "TX_KSTL",
            "PSKSTL",
            FamCorrectionCostCenter,
            includeNewActg: false,
            ct);
        var costCenterMatch = RequireSingleCorrectionRow(
            costCenterRows,
            row => Key(row, "PSWERK").Equals(FamCorrectionBusinessArea, StringComparison.Ordinal)
                && Key(row, "PSKSTL").Equals(FamCorrectionCostCenter, StringComparison.Ordinal),
            $"GB {FamCorrectionBusinessArea} / Kostenstelle {FamCorrectionCostCenter}");
        state["PSWERK"] = FamCorrectionBusinessArea;
        state["PSKSTL"] = FamCorrectionCostCenter;
        state["TX_KSTL"] = Key(costCenterMatch, "TX_KSTL");
        if (string.IsNullOrWhiteSpace(state["TX_KSTL"]))
            throw new ProcessConflictException(
                $"Oxaion lieferte für GB {FamCorrectionBusinessArea} / Kostenstelle {FamCorrectionCostCenter} keine Bezeichnung.");
        await SaveEventAsync(tx, "CORRECTION_DIALOG_COST_CENTER_VALIDATED",
            $"GB {FamCorrectionBusinessArea} / KST {FamCorrectionCostCenter} was resolved through the confirmed LB20115J/US11001R F4 path.", ct);

        return state;
    }

    private async Task<IReadOnlyList<XElement>> ReadCorrectionF4RowsAsync(
        OxaionSession session,
        Dictionary<string, string> positionState,
        string expectedProgram,
        string fields,
        string plainField,
        string masterField,
        string search,
        bool includeNewActg,
        CancellationToken ct)
    {
        var lookup = await session.CallAsync("LB20115J", "*F4", positionState, ct);
        OxaionSession.AssertNoFcod(lookup);
        var listSsid = Get(lookup.Dta, "SSID");
        if (string.IsNullOrWhiteSpace(listSsid)
            || !string.Equals(Get(lookup.Dta, "PGMN"), expectedProgram, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"LB20115J F4 for {masterField} did not open the confirmed {expectedProgram} list.");

        var header = Dict(
            ("FLD", fields),
            ("CPY-FRSSID", ""),
            ("NOHWPgm", expectedProgram.EndsWith("R", StringComparison.Ordinal)
                ? expectedProgram[..^1]
                : expectedProgram),
            ("SSID", listSsid));
        if (!string.IsNullOrWhiteSpace(plainField))
            header["PFLD"] = plainField;
        OxaionSession.AssertNoFcod(await session.CallAsync(expectedProgram, "*GETHDR", header, ct));

        var first = Dict(
            ("FLD", fields),
            ("SSID", listSsid),
            ("mode", "replace"));
        if (!string.IsNullOrWhiteSpace(plainField))
            first["PFLD"] = plainField;
        if (!string.IsNullOrWhiteSpace(search))
            first["SEARCH"] = search;
        if (includeNewActg)
            first["NEW_ACTG"] = "TRUE";

        return await ReadCompleteF4RowsAsync(session, expectedProgram, listSsid, first, ct);
    }

    private static XElement RequireSingleCorrectionRow(
        IReadOnlyList<XElement> rows,
        Func<XElement, bool> predicate,
        string description)
    {
        var matches = rows.Where(predicate).ToList();
        if (matches.Count != 1)
            throw new ProcessConflictException(
                $"{description} wurde in der bestätigten Oxaion-Auswahlliste nicht eindeutig gefunden (Treffer: {matches.Count}).");
        return matches[0];
    }

    private static string Key(XElement row, string name)
        => row.Element("KEY")?.Element(name)?.Value.Trim() ?? "";

    private Dictionary<string, string> CorrectionFields(
        SeparateOperation tx,
        DateOnly bookingDate,
        (string Operator, string BookingText) op,
        string bookingKey,
        string article,
        string articleText,
        string warehouse,
        string warehouseText,
        string batch,
        string q1,
        string q2,
        string first)
    {
        return Dict(
            ("PSANWG", "LBS"),
            ("PSBGKZ", "MB"),
            ("PSBGNR", tx.DocumentNo!),
            ("PSBGDT", Iso(bookingDate)),
            ("PSBGTX", op.BookingText),
            ("TX_BGT1", op.Operator),
            ("PSFIRM", _options.Firm),
            ("PSWERK", FamCorrectionBusinessArea),
            ("PSPOSI", "1"),
            ("PSBWKZ", bookingKey),
            ("TX_B1SB01", ExpectedCorrectionStockDirection(bookingKey)),
            ("PSIDNR", article),
            ("PSLAGO", warehouse),
            ("PSLAPL", ""),
            ("PSPONR", batch),
            ("PSBMN1", q1),
            ("PSBMN2", q2),
            ("PSKSTL", FamCorrectionCostCenter),
            ("TX_FIRST", first),
            ("KEYTYPE", CorrectionDialogKeyType),
            ("mode", "merge"));
    }

    private async Task VerifyAndCloseCorrectionAsync(
        OxaionSession session,
        SeparateOperation tx,
        string bookingKey,
        string article,
        string warehouse,
        string batch,
        decimal quantityKg,
        CancellationToken ct)
    {
        await SaveEventAsync(tx, "CORRECTION_VERIFYING",
            "Reopening Oxaion correction document for exact movement verification.", ct);
        var context = await OpenExistingDocumentAsync(session, tx, ct);
        Exception? failure = null;
        try
        {
            var rows = MixBookingService.ParseMovements(context.List.Xml);
            tx.LastMovements = rows.ToList();
            if (!CorrectionMovementComplete(rows, bookingKey, article, warehouse, batch, quantityKg, out var message))
                failure = new InvalidOperationException("Final correction verification failed: " + message);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        try
        {
            OxaionSession.AssertNoFcod(await session.CallAsync(
                "LB20100J", "*END",
                Dict(("KOBGNR", tx.DocumentNo!), ("KEYTYPE", "LKOPF")),
                CancellationToken.None));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Oxaion correction document {tx.DocumentNo} may already contain the movement, but the verification view could not be closed safely. " +
                "Do not rebook; check the Oxaion lock and movement manually.", ex);
        }

        if (failure is not null) throw failure;

        tx.Status = TransactionStatuses.Success;
        await SaveEventAsync(tx, "SUCCESS",
            $"Oxaion correction document {tx.DocumentNo} verified with exactly one {bookingKey} movement and explicitly closed.",
            CancellationToken.None);
    }
}
