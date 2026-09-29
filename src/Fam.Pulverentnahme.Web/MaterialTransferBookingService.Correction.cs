using System.Globalization;

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
            ("PSBMN1", "0,000"),
            ("PSBMN2", "0,000"),
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

        var description = bookingKey == "I2"
            ? "Bestandskorr. Abgang (Schwund)"
            : "Bestandskorrektur Zugang";
        var expectedDirection = ExpectedCorrectionStockDirection(bookingKey);

        // Fresh JET recordings from 2026-09-29 show that the successful I1/I2 dialog
        // sends the final accounting and direction already in the first PUTNEW:
        // PSWERK=21, PSKSTL=5100, TX_KSTL=3D-Druck, TX_B1SB01=1/2 and KEYTYPE=LKOPF.
        var firstPass = Merge(state, CorrectionFields(
            tx, bookingDate, op, bookingKey, article, articleText,
            warehouse, warehouseText, batch,
            "0,000", amount, "J"));
        firstPass["TX_BWKZ"] = description;

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
            ("I_PSIDNR", article),
            ("DEMO_IDNR", article),
            ("POIDNR", article),
            ("TX_IDNR", articleText ?? ""),
            ("PSLAGO", warehouse),
            ("TX_LAGO", TextOrCode(warehouseText, warehouse)),
            ("PSLAPL", ""),
            ("PSPONR", batch),
            ("PSBMN1", q1),
            ("PSBMN2", q2),
            ("PSKSTL", FamCorrectionCostCenter),
            ("TX_KSTL", FamCorrectionCostCenterText),
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
