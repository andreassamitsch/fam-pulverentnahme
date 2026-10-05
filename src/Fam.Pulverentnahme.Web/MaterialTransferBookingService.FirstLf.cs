using System.Globalization;

namespace Fam.Pulverentnahme.Web;

public sealed partial class MaterialTransferBookingService
{
    public async Task BookFillNewAsync(
        SeparateOperation tx,
        DateOnly bookingDate,
        string personnelNo,
        string personnelName,
        string bookingText,
        IReadOnlyList<TransferSpec> specs,
        CancellationToken ct)
    {
        if (specs.Count < 2
            || specs[0].Position != 1
            || !string.Equals(specs[0].BookingKey, "LF", StringComparison.Ordinal))
            throw new ArgumentException("Fill-new requires LF position 1 followed by the generated-MIX chain.");

        await using var session = await _oxaion.ConnectAsync(ct);
        try
        {
            var header = await NewHeaderAsync(session, ct);
            await SaveEventAsync(tx, "HEADER_SUBMITTING", "Creating Oxaion material document header.", ct);
            var op = OperatorContext(tx, personnelNo, personnelName, bookingText);
            var put = await session.CallAsync("LB20100J", "*PUTNEW", Merge(header, Dict(
                ("KOBGDT", Iso(bookingDate)),
                ("KOBGT1", op.DocumentText),
                ("KOBGTX", op.MatchCode),
                ("KOBGKZ", "MB"),
                ("KOFIRM", _options.Firm),
                ("KEYTYPE", "C_LKOPF"))), ct);
            OxaionSession.AssertNoFcod(put);

            var doc = Get(put.Dta, "KOBGNR");
            if (string.IsNullOrWhiteSpace(doc))
                throw new InvalidOperationException("LB20100J *PUTNEW did not return KOBGNR.");

            tx.DocumentNo = doc;
            tx.HeaderDta = put.Dta;
            tx.Status = TransactionStatuses.SendingToOxaion;
            await SaveEventAsync(tx, "HEADER_CREATED", $"Oxaion material document {doc} created.", ct);

            var context = await OpenExistingDocumentAsync(session, tx, ct);
            var firstValidated = await AddFirstLfPositionAsync(
                session, tx, context.Ssid, bookingDate, op, specs[0], ct);

            var refreshed = await ReadDocumentListAsync(session, context.Ssid, ct);
            var previous = TargetAccessStateFromRows(specs[0], refreshed.Xml, firstValidated);

            for (var i = 1; i < specs.Count; i++)
            {
                var spec = specs[i];
                var validated = await AddPositionAsync(
                    session, tx, context.Ssid, previous, bookingDate, op, spec, ct);
                if (i + 1 >= specs.Count) continue;

                refreshed = await ReadDocumentListAsync(session, context.Ssid, ct);
                previous = TargetAccessStateFromRows(spec, refreshed.Xml, validated);
            }

            await SaveEventAsync(tx, "ENDING", "Closing Oxaion material document.", ct);
            OxaionSession.AssertNoFcod(await session.CallAsync(
                "LB20100J", "*END", Dict(("KOBGNR", tx.DocumentNo), ("KEYTYPE", "LKOPF")), ct));
            await VerifyAndCloseAsync(session, tx, specs, ct);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tx.DocumentNo))
            {
                try
                {
                    await session.CallAsync(
                        "LB20100J",
                        "*END",
                        Dict(("KOBGNR", tx.DocumentNo!), ("KEYTYPE", "LKOPF")),
                        CancellationToken.None);
                }
                catch
                {
                    // Best-effort close only. Never turn a close failure into a blind retry.
                }
            }
        }
    }

    // First-position LF replay reconstructed from the successful 2026-09-08 Oxaion JET trace
    // "aus lager in tanklager buchen". This is intentionally separate from continuation
    // positions: an empty material document requires LB20115J *LOAD plus a complete position-1
    // seed before *NEW. Reusing the continuation seed caused BEL1422 after the header was created.
    private async Task<Dictionary<string, string>> AddFirstLfPositionAsync(
        OxaionSession session,
        SeparateOperation tx,
        string ssid,
        DateOnly bookingDate,
        OxaionDocumentTexts op,
        TransferSpec spec,
        CancellationToken ct)
    {
        if (spec.Position != 1 || !string.Equals(spec.BookingKey, "LF", StringComparison.Ordinal))
            throw new InvalidOperationException("First-LF replay may only be used for LF position 1.");

        await SaveEventAsync(tx, "POSITION_1_VALIDATING", "Validating first LF -> LE position on empty material document.", ct);

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
            ("KEYTYPE", "C_LKOPF"),
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

        await SaveEventAsync(tx, "POSITION_1_NEW_SENT", "Submitting LB20115J *NEW for first LF position.", ct);
        var created = await session.CallAsync("LB20115J", "*NEW", seed, ct);
        OxaionSession.AssertNoFcod(created);
        var state = Merge(seed, created.Dta);
        await SaveEventAsync(tx, "POSITION_1_NEW_OK", "First LF position shell loaded by Oxaion.", ct);

        var amount = FormatQty(spec.QuantityKg);

        // Captured JET sequence deliberately validates the source first while destination is still
        // blank. Oxaion answers LAG1515 (target warehouse required); this is an expected
        // intermediate validation response, not the final booking result.
        var sourceOnly = Merge(state, PositionFields(tx, bookingDate, op, spec, "0,000", amount, "J"));
        sourceOnly["TX_LAG2"] = "";
        sourceOnly["TX_LAGO2"] = "";
        sourceOnly["TX_LAP2"] = "";
        sourceOnly["TX_PON2"] = "";

        await SaveEventAsync(tx, "POSITION_1_SOURCE_VALIDATING", "Validating LF source before target warehouse is applied.", ct);
        var sourceResult = await session.CallAsync("LB20115J", "*PUTNEW", sourceOnly, ct);
        var intermediateCode = FirstText(sourceResult.Xml, "FCOD");
        if (!string.Equals(intermediateCode, "LAG1515", StringComparison.OrdinalIgnoreCase))
        {
            OxaionSession.AssertNoFcod(sourceResult);
            throw new InvalidOperationException(
                "First LF source validation did not return the confirmed intermediate Oxaion state LAG1515.");
        }
        state = Merge(sourceOnly, sourceResult.Dta);

        // Destination keys are validated separately against the confirmed Oxaion F4 lists before
        // they are sent as effective booking keys.
        await ValidateLfDestinationAsync(session, state, spec, ct);

        var withTarget = Merge(state, PositionFields(tx, bookingDate, op, spec, amount, amount, "J"));
        await SaveEventAsync(tx, "POSITION_1_TARGET_VALIDATING", "Validating LF source and target warehouse.", ct);
        var targetResult = await session.CallAsync("LB20115J", "*PUTNEW", withTarget, ct);
        OxaionSession.AssertNoFcod(targetResult);
        var tcode = FirstText(targetResult.Xml, "TCODE");
        if (!string.Equals(tcode, "WIN3", StringComparison.Ordinal))
            throw new InvalidOperationException("First LF target validation did not return the confirmed TCODE=WIN3 state.");
        state = Merge(withTarget, targetResult.Dta);

        var win = await session.CallAsync("LB20115J", "*LOADWIN3", Dict(
            ("NOHWPgm", "LB201153"),
            ("NoHints", "")), ct);
        OxaionSession.AssertNoFcod(win);
        state = Merge(state, win.Dta);

        var final = Merge(state, PositionFields(tx, bookingDate, op, spec, amount, amount, "N"));
        final["NoVPDialog"] = "true";
        final["NoWinSnnr"] = "true";
        final["NoWindow"] = "true";

        await SaveEventAsync(tx, "POSITION_1_FINAL_VALIDATING", "Submitting final LB20115J *PUTNEW for first LF position.", ct);
        var finalResult = await session.CallAsync("LB20115J", "*PUTNEW", final, ct);
        OxaionSession.AssertNoFcod(finalResult);
        var validated = Merge(final, finalResult.Dta);

        tx.Status = TransactionStatuses.SendingToOxaion;
        await SaveEventAsync(tx, "POSITION_1_UPD_SENT", "Submitting LB20110R *UPD for first LF position.", ct);
        var update = await session.CallAsync("LB20110R", "*UPD", Merge(validated, Dict(
            ("SSID", ssid),
            ("mode", "update"),
            ("KEYTYPE", "C_LKOPF"))), ct);
        OxaionSession.AssertNoFcod(update);

        var rows = MixBookingService.ParseMovements(update.Xml)
            .Where(r => r.Position == "1")
            .ToList();
        if (!MovementsComplete(new[] { spec }, rows, out var message))
            throw new InvalidOperationException("First LF/LE movement pair was not confirmed exactly: " + message);

        await SaveEventAsync(tx, "POSITION_1_CONFIRMED", "First LF -> LE position confirmed by Oxaion.", ct);
        return validated;
    }
}
