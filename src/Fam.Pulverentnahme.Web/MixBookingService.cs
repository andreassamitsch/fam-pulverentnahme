using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace Fam.Pulverentnahme.Web;

public sealed class SimulatedAnswerLossException(string stage) : Exception($"Simulated answer loss after {stage}.");

public sealed class MixBookingService
{
    private readonly OxaionClient _oxaion;
    private readonly JsonTransactionStore _store;
    private readonly PrototypeOptions _prototype;
    private readonly OxaionOptions _oxaionOptions;

    public MixBookingService(
        OxaionClient oxaion,
        JsonTransactionStore store,
        IOptions<PrototypeOptions> prototype,
        IOptions<OxaionOptions> oxaionOptions)
    {
        _oxaion = oxaion;
        _store = store;
        _prototype = prototype.Value;
        _oxaionOptions = oxaionOptions.Value;
    }

    public async Task<MixTransaction> ExecuteAsync(RealMixRequest request, CancellationToken ct)
    {
        ValidateRequest(request);
        var gate = _store.GetLock(request.ClientOperationId);
        await gate.WaitAsync(ct);
        try
        {
            var existing = await _store.GetAsync(request.ClientOperationId, ct);
            if (existing is not null) return existing;

            var tx = new MixTransaction
            {
                ClientOperationId = request.ClientOperationId,
                Request = request,
                Status = TransactionStatuses.Created,
                Stage = "CREATED",
                Message = "Transaction created."
            };
            await SaveEventAsync(tx, "CREATED", "Transaction created.", ct);
            try { await ExecuteNewTransactionAsync(tx, ct); }
            catch (OxaionRejectedException ex) { tx.Status = TransactionStatuses.Rejected; await SaveEventAsync(tx, "REJECTED", ex.Message, ct); }
            catch (Exception ex) when (ex is OxaionTransportException or SimulatedAnswerLossException) { tx.Status = TransactionStatuses.Uncertain; await SaveEventAsync(tx, "UNCERTAIN", ex.Message, ct); }
            catch (Exception ex) { tx.Status = TransactionStatuses.ManualReviewRequired; await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED", ex.Message, ct); }
            return tx;
        }
        finally { gate.Release(); }
    }

    public Task<MixTransaction?> GetAsync(string id, CancellationToken ct) => _store.GetAsync(id, ct);

    public async Task<MixTransaction> ReconcileAsync(string id, CancellationToken ct)
    {
        var gate = _store.GetLock(id);
        await gate.WaitAsync(ct);
        try
        {
            var tx = await _store.GetAsync(id, ct) ?? throw new KeyNotFoundException("Transaction not found.");
            if (tx.Status == TransactionStatuses.Success) return tx;
            if (string.IsNullOrWhiteSpace(tx.DocumentNo) || tx.HeaderDta is null)
            {
                tx.Status = TransactionStatuses.ManualReviewRequired;
                await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED", "No confirmed Oxaion document number is available. Do not retry blindly.", ct);
                return tx;
            }

            try
            {
                await using var session = await _oxaion.ConnectAsync(ct);
                var context = await OpenExistingDocumentAsync(session, tx, ct);
                var analysis = AnalyzeMovements(tx.Request, ParseMovements(context.List.Xml));
                tx.LastMovements = analysis.Rows.ToList();

                if (analysis.Status == "COMPLETE")
                {
                    await FinalizeAndVerifyAsync(session, tx, ct);
                }
                else if (analysis.Status == "NONE")
                {
                    await SaveEventAsync(tx, "RECOVERY_POSITION_1", "No movements found; rebuilding position 1 in existing document.", ct);
                    var p1 = await AddPosition1Async(session, tx, context.Ssid, ct);
                    var refreshed = await ReadDocumentListAsync(session, context.Ssid, ct);
                    var continuation = TargetLnStateFromRows(tx.Request.TargetBatch, 1, refreshed.Xml, p1);
                    await AddRemainingSourcePositionsAsync(session, tx, context.Ssid, 2, continuation, ct);
                    await FinalizeAndVerifyAsync(session, tx, ct);
                }
                else if (analysis.Status == "PREFIX_COMPLETE")
                {
                    var completed = analysis.CompletedPositions;
                    var total = TotalPositions(tx.Request);
                    var next = completed + 1;
                    var fallback = ValidatedStateForPosition(tx, completed)
                        ?? throw new InvalidOperationException($"Position {completed} is confirmed but its validated continuation state is missing. Automatic continuation is blocked.");
                    await SaveEventAsync(tx, $"RECOVERY_POSITION_{next}", $"Positions 1 to {completed} are confirmed; continuing positions {next} to {total} only.", ct);
                    var continuation = TargetLnStateFromRows(tx.Request.TargetBatch, completed, context.List.Xml, fallback);
                    await AddRemainingSourcePositionsAsync(session, tx, context.Ssid, next, continuation, ct);
                    await FinalizeAndVerifyAsync(session, tx, ct);
                }
                else
                {
                    tx.Status = TransactionStatuses.ManualReviewRequired;
                    await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED", "Unexpected partial Oxaion document state; automatic continuation blocked.", ct);
                }
            }
            catch (OxaionRejectedException ex) { tx.Status = TransactionStatuses.Rejected; await SaveEventAsync(tx, "REJECTED", ex.Message, ct); }
            catch (Exception ex) when (ex is OxaionTransportException or SimulatedAnswerLossException) { tx.Status = TransactionStatuses.Uncertain; await SaveEventAsync(tx, "UNCERTAIN", ex.Message, ct); }
            catch (Exception ex) { tx.Status = TransactionStatuses.ManualReviewRequired; await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED", ex.Message, ct); }
            return tx;
        }
        finally { gate.Release(); }
    }

    private async Task ExecuteNewTransactionAsync(MixTransaction tx, CancellationToken ct)
    {
        tx.Status = TransactionStatuses.Validating;
        await SaveEventAsync(tx, "CONNECTING", "Connecting to Oxaion STAGING.", ct);
        await using var session = await _oxaion.ConnectAsync(ct);
        var header = await NewHeaderAsync(session, ct);
        var document = await PersistHeaderAsync(session, tx, header, ct);
        var p1 = await AddPosition1Async(session, tx, document.Ssid, ct);
        var refreshed = await ReadDocumentListAsync(session, document.Ssid, ct);
        var continuation = TargetLnStateFromRows(tx.Request.TargetBatch, 1, refreshed.Xml, p1);
        await AddRemainingSourcePositionsAsync(session, tx, document.Ssid, 2, continuation, ct);
        await FinalizeAndVerifyAsync(session, tx, ct);
    }

    private async Task<Dictionary<string, string>> NewHeaderAsync(OxaionSession session, CancellationToken ct)
    {
        var load = await session.CallAsync("LB20100J", "*LOADNEW", Dict(
            ("ISSID", "HTTPWEB" + Guid.NewGuid().ToString("N")),
            ("NOHWPgm", "LB20100"),
            ("SSID", ""),
            ("KOBGNR", ""),
            ("KEYTYPE", "C_LKOPF")), ct);
        return (await session.CallAsync("LB20100J", "*NEW", Merge(load.Dta, Dict(("KEYTYPE", "C_LKOPF"))), ct)).Dta;
    }

    private async Task<DocumentContext> PersistHeaderAsync(OxaionSession session, MixTransaction tx, Dictionary<string, string> header, CancellationToken ct)
    {
        tx.Status = TransactionStatuses.SendingToOxaion;
        await SaveEventAsync(tx, "HEADER_SUBMITTING", "Creating Oxaion material document header.", ct);
        var op = OperatorContext(tx);
        var input = Merge(header, Dict(
            ("KOBGDT", Iso(tx.Request.BookingDate)),
            ("KOBGT1", op.Operator),
            ("KOBGTX", op.BookingText),
            ("KOBGKZ", "MB"),
            ("KOFIRM", _oxaionOptions.Firm),
            ("KEYTYPE", "C_LKOPF")));
        var put = await session.CallAsync("LB20100J", "*PUTNEW", input, ct);
        OxaionSession.AssertNoFcod(put);
        var doc = Get(put.Dta, "KOBGNR");
        if (string.IsNullOrWhiteSpace(doc)) throw new InvalidOperationException("LB20100J *PUTNEW did not return KOBGNR.");
        tx.DocumentNo = doc;
        tx.HeaderDta = put.Dta;
        await SaveEventAsync(tx, "HEADER_CREATED", $"Oxaion material document {doc} created.", ct);
        return await OpenExistingDocumentAsync(session, tx, ct);
    }

    private async Task<DocumentContext> OpenExistingDocumentAsync(OxaionSession session, MixTransaction tx, CancellationToken ct)
    {
        await session.CallAsync("LB20100J", "*OPEN", Dict(("KOBGNR", tx.DocumentNo!), ("KEYTYPE", "LKOPF"), ("noAutCheck", "")), ct);
        var shortResult = await session.CallAsync("LB20090J", "*SHORT", Merge(tx.HeaderDta!, Dict(("PSANWG", "LBS"), ("PSBGNR", tx.DocumentNo!), ("KEYTYPE", "C_LKOPF"))), ct);
        var ssid = Get(shortResult.Dta, "SSID");
        if (string.IsNullOrWhiteSpace(ssid)) throw new InvalidOperationException("LB20090J *SHORT did not return SSID.");
        await session.CallAsync("LB20110R", "*GETHDR", Dict(("SSID", ssid)), ct);
        var list = await ReadDocumentListAsync(session, ssid, ct);
        return new DocumentContext(ssid, list);
    }

    private static Task<OxaionCallResult> ReadDocumentListAsync(OxaionSession session, string ssid, CancellationToken ct) =>
        session.CallAsync("LB20110R", "*FIRSTLIST", Dict(("FLD", ""), ("PFLD", ""), ("SSID", ssid), ("mode", "replace")), ct);

    private async Task<Dictionary<string, string>> AddPosition1Async(OxaionSession session, MixTransaction tx, string ssid, CancellationToken ct)
    {
        await SaveEventAsync(tx, "POSITION_1_VALIDATING", "Validating old MIX -> new MIX.", ct);
        var r = tx.Request;
        var ts = OxaionTimestamp();
        var seed = Merge(tx.HeaderDta!, Dict(
            ("ANWG", "LBS"), ("PSANWG", "LBS"), ("PSBGNR", tx.DocumentNo!), ("PSBGDT", Iso(r.BookingDate)),
            ("PSBGKZ", "MB"), ("PSFIRM", _oxaionOptions.Firm), ("PSPOSI", "1"), ("PSKOPO", "0"),
            ("PSBGZT", ts), ("PSBMN1", "0,000"), ("PSBMN2", "0,000"), ("KEYTYPE", "C_LKOPF"),
            ("SSID", ssid), ("SNR", "1"), ("WSTR", "1"), ("PGMN", "LB20110R"), ("MTYPE", "*PGM"),
            ("NAME", "UPOSTP.POPONR"), ("MC-Modus", "true"), ("NoModDlg", "true"),
            ("keyFields", "PSBGNR PSPOSI PSKOPO PSBGZT"), ("keyFirm", "FIRM")));

        var created = await session.CallAsync("LB20115J", "*NEW", seed, ct);
        var state = Merge(seed, created.Dta);
        var context = PositionContext.Position1(tx);
        var amount = FormatQty(r.OldMixAmountKg);

        var p0 = ApplyPosition(state, tx, context, "", "0,000", amount, true, ts, Iso(r.ProductionDate));
        state = Merge(p0, (await session.CallAsync("LB20115J", "*PUTNEW", p0, ct)).Dta);
        var pLn = ApplyPosition(state, tx, context, "LN", "0,000", amount, true, ts, Iso(r.ProductionDate));
        state = Merge(pLn, (await session.CallAsync("LB20115J", "*PUTNEW", pLn, ct)).Dta);
        var pLm = ApplyPosition(state, tx, context, "LM", "0,000", amount, true, ts, Iso(r.ProductionDate));
        state = Merge(pLm, (await session.CallAsync("LB20115J", "*PUTNEW", pLm, ct)).Dta);
        state = Merge(state, (await session.CallAsync("LB20115J", "*LOADWIN3", Dict(("NOHWPgm", "LB201153"), ("NoHints", "")), ct)).Dta);

        var final = ApplyPosition(state, tx, context, "LM", amount, amount, false, ts, Iso(r.ProductionDate));
        foreach (var key in new[] { "TX_B1BSUB", "TX_B1CHUB", "TX_WLO1BZ", "TX_WLO2", "TX__LBBSUB" })
            if (state.TryGetValue(key, out var value)) final[key] = value;
        final["NoVPDialog"] = "true";
        final["NoWinSnnr"] = "true";
        final["NoWindow"] = "true";

        var finalResult = await session.CallAsync("LB20115J", "*PUTNEW", final, ct);
        OxaionSession.AssertNoFcod(finalResult);
        var validated = Merge(final, finalResult.Dta);
        tx.Position1ValidatedState = validated;
        RememberValidatedState(tx, 1, validated);

        tx.Status = TransactionStatuses.SendingToOxaion;
        await SaveEventAsync(tx, "POSITION_1_UPD_SENT", "Submitting LB20110R *UPD for position 1.", ct);
        var update = await session.CallAsync("LB20110R", "*UPD", Merge(validated, Dict(("SSID", ssid), ("mode", "update"), ("KEYTYPE", "C_LKOPF"))), ct);
        OxaionSession.AssertNoFcod(update);
        VerifyContainsLmLn(update);
        MaybeSimulatePosition(r, 1);

        tx.Status = TransactionStatuses.Position1Confirmed;
        await SaveEventAsync(tx, "POSITION_1_CONFIRMED", "Position 1 confirmed by Oxaion.", ct);
        return validated;
    }

    private async Task AddRemainingSourcePositionsAsync(
        OxaionSession session,
        MixTransaction tx,
        string ssid,
        int startPosition,
        Dictionary<string, string> previous,
        CancellationToken ct)
    {
        var sources = MixRequestLogic.Sources(tx.Request);
        var totalPositions = sources.Count + 1;
        if (startPosition < 2 || startPosition > totalPositions) return;

        for (var position = startPosition; position <= totalPositions; position++)
        {
            var source = sources[position - 2];
            var validated = await AddContinuationPositionAsync(session, tx, ssid, previous, position, source, ct);
            if (position >= totalPositions) continue;

            var refreshed = await ReadDocumentListAsync(session, ssid, ct);
            previous = TargetLnStateFromRows(tx.Request.TargetBatch, position, refreshed.Xml, validated);
        }
    }

    private async Task<Dictionary<string, string>> AddContinuationPositionAsync(
        OxaionSession session,
        MixTransaction tx,
        string ssid,
        Dictionary<string, string> previous,
        int position,
        AdditionalPowderSource source,
        CancellationToken ct)
    {
        var stagePrefix = $"POSITION_{position}";
        await SaveEventAsync(tx, stagePrefix + "_VALIDATING", $"Validating replenishment batch {source.Batch} -> same new MIX as position {position}.", ct);

        var r = tx.Request;
        var op = OperatorContext(tx);
        var positionText = position.ToString(CultureInfo.InvariantCulture);
        var seed = Merge(previous, Dict(
            ("SSID", ssid), ("SNR", positionText), ("WSTR", "1"), ("PGMN", "LB20110R"), ("MTYPE", "*PGM"),
            ("NAME", "UPOSTP.POPONR"), ("MC-Modus", "true"), ("NoModDlg", "true"),
            ("keyFields", "PSBGNR PSPOSI PSKOPO PSBGZT"), ("keyFirm", "FIRM"), ("KEYTYPE", "C_LKOPF")));
        var created = await session.CallAsync("LB20115J", "*NEW", seed, ct);
        OxaionSession.AssertNoFcod(created);
        var state = Merge(seed, created.Dta);

        var first = Merge(state, ContinuationFields(tx, op, positionText, source, "0,000", FormatQty(source.AmountKg), "J"));
        var r1 = await session.CallAsync("LB20115J", "*PUTNEW", first, ct);
        OxaionSession.AssertNoFcod(r1);
        state = Merge(first, r1.Dta);

        var tcode = FirstText(r1.Xml, "TCODE");
        var txFirst = FirstText(r1.Xml, "TX_FIRST");
        var q1 = FirstText(r1.Xml, "PSBMN1");
        var q2 = FirstText(r1.Xml, "PSBMN2");
        var newTs = FirstText(r1.Xml, "PSBGZT");
        Dictionary<string, string> validated;

        if (tcode == "WIN3")
        {
            state = Merge(state, (await session.CallAsync("LB20115J", "*LOADWIN3", Dict(("NOHWPgm", "LB201153"), ("NoHints", "")), ct)).Dta);
            var final = Merge(state, ContinuationFields(tx, op, positionText, source, FormatQty(source.AmountKg), FormatQty(source.AmountKg), "N"));
            final["NoVPDialog"] = "true";
            final["NoWinSnnr"] = "true";
            final["NoWindow"] = "true";
            var fr = await session.CallAsync("LB20115J", "*PUTNEW", final, ct);
            OxaionSession.AssertNoFcod(fr);
            validated = Merge(final, fr.Dta);
        }
        else if (txFirst == "N"
                 && !string.IsNullOrWhiteSpace(newTs)
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

        RememberValidatedState(tx, position, validated);
        tx.Status = TransactionStatuses.SendingToOxaion;
        await SaveEventAsync(tx, stagePrefix + "_UPD_SENT", $"Submitting LB20110R *UPD for position {position} ({source.Batch}).", ct);
        var update = await session.CallAsync("LB20110R", "*UPD", Merge(validated, Dict(("SSID", ssid), ("mode", "update"), ("KEYTYPE", "C_LKOPF"))), ct);
        OxaionSession.AssertNoFcod(update);
        VerifyContainsLmLn(update);
        MaybeSimulatePosition(r, position);

        tx.Status = position == 2 ? TransactionStatuses.Position2Confirmed : stagePrefix + "_CONFIRMED";
        await SaveEventAsync(tx, stagePrefix + "_CONFIRMED", $"Position {position} ({source.Batch}) confirmed by Oxaion.", ct);
        return validated;
    }

    private Dictionary<string, string> ContinuationFields(
        MixTransaction tx,
        (string Operator, string BookingText) op,
        string position,
        AdditionalPowderSource source,
        string q1,
        string q2,
        string txFirst)
    {
        var r = tx.Request;
        var fields = Dict(
            ("PSANWG", "LBS"), ("PSBGKZ", "MB"), ("PSBGNR", tx.DocumentNo!), ("PSBGDT", Iso(r.BookingDate)),
            ("PSBGTX", op.BookingText), ("TX_BGT1", op.Operator), ("PSFIRM", _oxaionOptions.Firm),
            ("PSPOSI", position), ("PSBWKZ", "LM"), ("PSIDNR", r.Article), ("I_PSIDNR", r.Article),
            ("DEMO_IDNR", r.Article), ("POIDNR", r.Article), ("I_TX_IDN2", ""), ("I_TX_PCKMM", ""),
            ("I_TX_PCKMS", ""), ("I_TX_PCKMZ", ""), ("PSLAGO", source.Warehouse), ("PSPONR", source.Batch),
            ("TX_LAG2", r.TargetWarehouse), ("TX_PON2", r.TargetBatch), ("PSLAPL", source.StorageBin ?? ""),
            ("PSPRDT", ""), ("PSBMN1", q1), ("PSBMN2", q2), ("TX_FIRST", txFirst),
            ("TX_LAGO", TextOrCode(source.WarehouseText, source.Warehouse)),
            ("TX_LAGO2", TextOrCode(r.TargetWarehouseText, r.TargetWarehouse)), ("TX_PDBZ2", "pro 1"),
            ("KEYTYPE", "C_LKOPF"), ("mode", "merge"));
        if (!string.IsNullOrWhiteSpace(r.TargetStorageBin)) fields["TX_LAP2"] = r.TargetStorageBin;
        return fields;
    }

    private async Task FinalizeAndVerifyAsync(OxaionSession session, MixTransaction tx, CancellationToken ct)
    {
        await SaveEventAsync(tx, "ENDING", "Closing Oxaion material document.", ct);
        var end = await session.CallAsync("LB20100J", "*END", Dict(("KOBGNR", tx.DocumentNo!), ("KEYTYPE", "LKOPF")), ct);
        OxaionSession.AssertNoFcod(end);

        var expectedMovements = TotalPositions(tx.Request) * 2;
        await SaveEventAsync(tx, "VERIFYING", $"Reopening document and verifying {expectedMovements} movements.", ct);
        var reopened = await OpenExistingDocumentAsync(session, tx, ct);
        var analysis = AnalyzeMovements(tx.Request, ParseMovements(reopened.List.Xml));
        tx.LastMovements = analysis.Rows.ToList();
        if (analysis.Status != "COMPLETE") throw new InvalidOperationException("Final verification failed: " + analysis.Message);

        // OPEN for the read-only final verification can hold the Oxaion material document lock.
        // The app-tunnel disconnect is not used as a substitute for the proven LB20100J *END.
        // Cleanup must not be cancelled just because the browser request was aborted after the
        // booking was already fully persisted and verified.
        try
        {
            var verificationEnd = await session.CallAsync(
                "LB20100J",
                "*END",
                Dict(("KOBGNR", tx.DocumentNo!), ("KEYTYPE", "LKOPF")),
                CancellationToken.None);
            OxaionSession.AssertNoFcod(verificationEnd);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Oxaion document {tx.DocumentNo} was fully booked and verified, but the final verification view could not be closed safely. Do not rebook; check the Oxaion lock manually.",
                ex);
        }

        await SaveEventAsync(tx, "VERIFICATION_CLOSED", $"Oxaion document {tx.DocumentNo} explicitly closed after final verification.", CancellationToken.None);
        tx.Status = TransactionStatuses.Success;
        await SaveEventAsync(tx, "SUCCESS", $"Oxaion document {tx.DocumentNo} verified with exactly {expectedMovements} expected LM/LN movements across {TotalPositions(tx.Request)} positions and explicitly closed.", CancellationToken.None);
    }

    private Dictionary<string, string> ApplyPosition(
        Dictionary<string, string> baseState,
        MixTransaction tx,
        PositionContext c,
        string bwkz,
        string q1,
        string q2,
        bool firstPass,
        string ts,
        string prodDate)
    {
        var op = OperatorContext(tx);
        var o = Dict(
            ("PSANWG", "LBS"), ("PSBGKZ", "MB"), ("PSBGNR", tx.DocumentNo!), ("PSBGDT", Iso(tx.Request.BookingDate)),
            ("PSBGZT", ts), ("PSBGTX", op.BookingText), ("TX_BGT1", op.Operator), ("PSFIRM", _oxaionOptions.Firm),
            ("PSPOSI", c.Position), ("PSIDNR", tx.Request.Article), ("I_PSIDNR", tx.Request.Article),
            ("DEMO_IDNR", tx.Request.Article), ("POIDNR", tx.Request.Article), ("PSLAGO", c.FromWarehouse),
            ("PSPONR", c.FromBatch), ("TX_LAG2", c.ToWarehouse), ("TX_PON2", c.ToBatch),
            ("PSBMN1", q1), ("PSBMN2", q2), ("PSBWKZ", bwkz), ("KEYTYPE", "C_LKOPF"), ("mode", "merge"),
            ("TX_B1BSUB", "LE"), ("TX_B1KZWK", "N"), ("TX_B1CHUB", "N"), ("TX_B1TLUB", "N"),
            ("TX_B1SB01", "2"), ("TX_MRKPRM", "N"), ("TX_MRKPONR", "J"), ("TX_TLWERK", "21"),
            ("TX_FAKT", "1,000000000"), ("TX_PDBZ", "pro 1"), ("TX_FEIG2", "N"), ("TX_BSTFPM", "N"),
            ("TX_PMWDM", "N"), ("TX_PMWDS", "N"), ("TX_UDIT", "N"), ("TX_SNPF", "0"),
            ("I_TX_IDN2", ""), ("I_TX_PCKMM", ""), ("I_TX_PCKMS", ""), ("I_TX_PCKMZ", ""),
            ("TX_IDNR", tx.Request.ArticleText), ("TX_WLO1", c.FromWarehouse), ("TX_WLO2", c.ToWarehouse),
            ("TX_LAGO", c.FromWarehouseText), ("TX_LAGO2", c.ToWarehouseText), ("TX_LAG1BZ", c.FromWarehouseText),
            ("TX_LAG2BZ", c.ToWarehouseText), ("TX_WLO1BZ", c.FromWarehouseText), ("TX_PDBZ2", "pro 1"),
            ("TX__LBBSUB", "Chargenumbuchung - Zugang"));
        o["PSLAPL"] = c.FromStorageBin;
        if (!string.IsNullOrWhiteSpace(c.ToStorageBin)) o["TX_LAP2"] = c.ToStorageBin;
        if (firstPass) o["TX_FIRST"] = "J";
        if (!string.IsNullOrWhiteSpace(prodDate)) o["PSPRDT"] = prodDate;
        return Merge(baseState, o);
    }

    private (string Operator, string BookingText) OperatorContext(MixTransaction tx)
    {
        var r = tx.Request;
        var operatorText = string.IsNullOrWhiteSpace(r.PersonnelName) ? $"PN {r.PersonnelNo}" : $"PN {r.PersonnelNo} | {r.PersonnelName}";
        var prefix = $"{tx.TransactionId[..12]}|PN{r.PersonnelNo}";
        var room = Math.Max(0, 50 - prefix.Length - 1);
        return (operatorText, room > 0 && !string.IsNullOrWhiteSpace(r.BookingText)
            ? prefix + "|" + r.BookingText[..Math.Min(room, r.BookingText.Length)]
            : prefix);
    }

    private async Task SaveEventAsync(MixTransaction tx, string stage, string message, CancellationToken ct)
    {
        tx.Stage = stage;
        tx.Message = message;
        tx.Events.Add(new TransactionEvent(DateTimeOffset.UtcNow, stage, message));
        await _store.SaveAsync(tx, ct);
    }

    private void MaybeSimulatePosition(RealMixRequest r, int position)
    {
        if (!_prototype.EnableFailureSimulation) return;
        var generic = $"after_position_{position}_upd";
        var legacy = position switch { 1 => "after_pos1_upd", 2 => "after_pos2_upd", _ => "" };
        if (string.Equals(r.SimulateFailure, generic, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrEmpty(legacy) && string.Equals(r.SimulateFailure, legacy, StringComparison.OrdinalIgnoreCase)))
            throw new SimulatedAnswerLossException(generic);
    }

    private static void VerifyContainsLmLn(OxaionCallResult result)
    {
        var keys = result.Xml.Descendants("LPSDAP.PSBWKZ").Select(x => x.Value.Trim()).ToHashSet(StringComparer.Ordinal);
        if (!keys.Contains("LM") || !keys.Contains("LN"))
            throw new InvalidOperationException("LB20110R *UPD did not return both LM and LN rows.");
    }

    private static void RememberValidatedState(MixTransaction tx, int position, Dictionary<string, string> state)
    {
        tx.PositionValidatedStates ??= new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        tx.PositionValidatedStates[position.ToString(CultureInfo.InvariantCulture)] = new Dictionary<string, string>(state, StringComparer.Ordinal);
    }

    private static Dictionary<string, string>? ValidatedStateForPosition(MixTransaction tx, int position)
    {
        if (tx.PositionValidatedStates is not null
            && tx.PositionValidatedStates.TryGetValue(position.ToString(CultureInfo.InvariantCulture), out var state))
            return state;
        return position == 1 ? tx.Position1ValidatedState : null;
    }

    private static Dictionary<string, string> TargetLnStateFromRows(
        string targetBatch,
        int position,
        XDocument xml,
        Dictionary<string, string> fallback)
    {
        var expectedPosition = position.ToString(CultureInfo.InvariantCulture);
        foreach (var row in xml.Descendants("ROW"))
        {
            if (row.Element("LPSDAP.PSBWKZ")?.Value.Trim() != "LN"
                || row.Element("LPSDAP.PSPONR")?.Value.Trim() != targetBatch)
                continue;

            var key = row.Element("KEY");
            var rowPosition = row.Element("LPSDAP.PSPOSI")?.Value.Trim();
            if (string.IsNullOrWhiteSpace(rowPosition)) rowPosition = key?.Element("PSPOSI")?.Value.Trim();
            if (!string.Equals(rowPosition, expectedPosition, StringComparison.Ordinal)) continue;

            var ts = key?.Element("PSBGZT")?.Value.Trim();
            if (string.IsNullOrWhiteSpace(ts)) continue;
            var state = new Dictionary<string, string>(fallback, StringComparer.Ordinal) { ["PSBGZT"] = ts, ["PSPOSI"] = expectedPosition };
            var kopo = key?.Element("PSKOPO")?.Value.Trim();
            if (!string.IsNullOrWhiteSpace(kopo)) state["PSKOPO"] = kopo;
            return state;
        }
        throw new InvalidOperationException($"Persisted LN target row for position {position} was not found.");
    }

    internal static IReadOnlyList<MovementRow> ParseMovements(XDocument xml)
    {
        var rows = new List<MovementRow>();
        foreach (var row in xml.Descendants("ROW"))
        {
            string V(string n) => row.Element(n)?.Value.Trim() ?? "";
            var key = row.Element("KEY");
            var pos = V("LPSDAP.PSPOSI");
            if (string.IsNullOrWhiteSpace(pos)) pos = key?.Element("PSPOSI")?.Value.Trim() ?? "";
            rows.Add(new MovementRow(
                pos,
                V("LPSDAP.PSBWKZ"),
                V("LPSDAP.PSIDNR").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "",
                V("LPSDAP.PSPONR"),
                V("LPSDAP.PSLAGO"),
                V("LPSDAP.PSLAPL"),
                ParseEu(V("LPSDAP.PSBMN1")),
                key?.Element("PSBGZT")?.Value.Trim() ?? ""));
        }
        return rows;
    }

    internal static ReconcileResult AnalyzeMovements(RealMixRequest r, IReadOnlyList<MovementRow> rows)
    {
        var expected = BuildExpected(r);
        var totalPositions = TotalPositions(r);
        if (rows.Count == 0)
            return new ReconcileResult("NONE", "CONTINUE_POSITION_1", "No persisted movements found.", rows, 0);

        var completed = 0;
        for (var position = 1; position <= totalPositions; position++)
        {
            var pair = expected.Where(e => e.Position == position.ToString(CultureInfo.InvariantCulture)).ToArray();
            var counts = pair.Select(e => rows.Count(row => Matches(row, e))).ToArray();
            if (counts.All(c => c == 1))
            {
                completed++;
                continue;
            }
            if (counts.All(c => c == 0)) break;
            return new ReconcileResult("INCONSISTENT", "MANUAL_REVIEW", $"Position {position} contains an incomplete or duplicate LM/LN pair.", rows, completed);
        }

        var laterExpected = expected.Where(e => int.Parse(e.Position, CultureInfo.InvariantCulture) > completed);
        if (laterExpected.Any(e => rows.Any(row => Matches(row, e))))
            return new ReconcileResult("INCONSISTENT", "MANUAL_REVIEW", "A later position exists although an earlier position is incomplete.", rows, completed);

        if (rows.Count != completed * 2)
            return new ReconcileResult("INCONSISTENT", "MANUAL_REVIEW", "Unexpected additional or unassignable movement rows are present.", rows, completed);

        if (completed == totalPositions)
            return new ReconcileResult("COMPLETE", "FINALIZE", $"All {totalPositions} positions with {rows.Count} expected LM/LN movements are present exactly once.", rows, completed);

        return new ReconcileResult(
            "PREFIX_COMPLETE",
            $"CONTINUE_POSITION_{completed + 1}",
            $"Positions 1 to {completed} are complete; position {completed + 1} and later positions are missing.",
            rows,
            completed);
    }

    private static IReadOnlyList<Expected> BuildExpected(RealMixRequest r)
    {
        var expected = new List<Expected>
        {
            new("1", "LM", r.Article, r.OldMixBatch, r.OldMixWarehouse, r.OldMixStorageBin, r.OldMixAmountKg),
            new("1", "LN", r.Article, r.TargetBatch, r.TargetWarehouse, r.TargetStorageBin, r.OldMixAmountKg)
        };
        var sources = MixRequestLogic.Sources(r);
        for (var i = 0; i < sources.Count; i++)
        {
            var p = (i + 2).ToString(CultureInfo.InvariantCulture);
            var source = sources[i];
            expected.Add(new Expected(p, "LM", r.Article, source.Batch, source.Warehouse, source.StorageBin, source.AmountKg));
            expected.Add(new Expected(p, "LN", r.Article, r.TargetBatch, r.TargetWarehouse, r.TargetStorageBin, source.AmountKg));
        }
        return expected;
    }

    private static bool Matches(MovementRow r, Expected e) =>
        r.Position == e.Position
        && r.BookingKey == e.BookingKey
        && r.Article == e.Article
        && r.Batch == e.Batch
        && r.Warehouse == e.Warehouse
        && (r.StorageBin ?? "") == (e.StorageBin ?? "")
        && Math.Abs(r.Quantity - e.Quantity) < 0.0005m;

    private static decimal ParseEu(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0m;
        var trimmed = s.Trim();
        var numeric = new string(trimmed.TakeWhile(ch => char.IsDigit(ch) || ch is '+' or '-' or ',' or '.').ToArray());
        if (string.IsNullOrWhiteSpace(numeric))
            throw new FormatException($"Oxaion quantity '{s}' does not start with a numeric value.");

        var x = numeric;
        if (x.Contains(',') && x.Contains('.'))
            x = x.LastIndexOf(',') > x.LastIndexOf('.') ? x.Replace(".", "").Replace(',', '.') : x.Replace(",", "");
        else if (x.Contains(','))
            x = x.Replace(',', '.');

        if (decimal.TryParse(x, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
            return value;
        throw new FormatException($"Oxaion quantity '{s}' could not be parsed safely.");
    }

    internal static void ValidateRequest(RealMixRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.ClientOperationId) || string.IsNullOrWhiteSpace(r.PersonnelNo) || string.IsNullOrWhiteSpace(r.Article))
            throw new ArgumentException("clientOperationId, PersonnelNo and Article are required.");
        if (string.IsNullOrWhiteSpace(r.OldMixWarehouse) || string.IsNullOrWhiteSpace(r.OldMixBatch) || string.IsNullOrWhiteSpace(r.TargetWarehouse) || string.IsNullOrWhiteSpace(r.TargetBatch))
            throw new ArgumentException("Old MIX source/target data are incomplete.");
        if (r.OldMixAmountKg <= 0) throw new ArgumentException("Old MIX amount must be > 0.");
        if (!MixRequestLogic.LegacyFirstSourceMatches(r))
            throw new ArgumentException("Legacy first replenishment source does not match additionalSources[0].");

        var sources = MixRequestLogic.Sources(r);
        if (sources.Count == 0) throw new ArgumentException("At least one replenishment batch is required.");
        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            if (string.IsNullOrWhiteSpace(source.Warehouse) || string.IsNullOrWhiteSpace(source.Batch))
                throw new ArgumentException($"Replenishment source {i + 1} is incomplete.");
            if (source.AmountKg <= 0) throw new ArgumentException($"Replenishment source {i + 1} amount must be > 0.");
            if (string.Equals(r.TargetBatch, source.Batch, StringComparison.Ordinal))
                throw new ArgumentException($"Target MIX batch must differ from replenishment source batch {i + 1}.");
        }
        if (string.Equals(r.TargetBatch, r.OldMixBatch, StringComparison.Ordinal))
            throw new ArgumentException("Target MIX batch must differ from old MIX batch.");
    }

    private static int TotalPositions(RealMixRequest request) => 1 + MixRequestLogic.Sources(request).Count;
    private static Dictionary<string, string> Dict(params (string Key, string Value)[] values) => values.ToDictionary(x => x.Key, x => x.Value ?? "", StringComparer.Ordinal);
    private static Dictionary<string, string> Merge(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
    {
        var result = new Dictionary<string, string>(left, StringComparer.Ordinal);
        foreach (var item in right) result[item.Key] = item.Value ?? "";
        return result;
    }
    private static string Get(IReadOnlyDictionary<string, string> values, string key) => values.TryGetValue(key, out var value) ? value : "";
    private static string FirstText(XDocument xml, string name) => xml.Descendants(name).FirstOrDefault()?.Value.Trim() ?? "";
    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string FormatQty(decimal value) => value.ToString("0.000", CultureInfo.GetCultureInfo("de-AT"));
    private static string OxaionTimestamp() => DateTime.Now.ToString("yyyy-MM-dd-HH.mm.ss.ffffff", CultureInfo.InvariantCulture);
    private static string TextOrCode(string text, string code) => string.IsNullOrWhiteSpace(text) ? code : text;

    private sealed record DocumentContext(string Ssid, OxaionCallResult List);
    private sealed record Expected(string Position, string BookingKey, string Article, string Batch, string Warehouse, string StorageBin, decimal Quantity);
    private sealed record PositionContext(
        string Position,
        string FromWarehouse,
        string FromWarehouseText,
        string FromStorageBin,
        string FromBatch,
        string ToWarehouse,
        string ToWarehouseText,
        string ToStorageBin,
        string ToBatch)
    {
        public static PositionContext Position1(MixTransaction tx) => new(
            "1",
            tx.Request.OldMixWarehouse,
            TextOrCode(tx.Request.OldMixWarehouseText, tx.Request.OldMixWarehouse),
            tx.Request.OldMixStorageBin ?? "",
            tx.Request.OldMixBatch,
            tx.Request.TargetWarehouse,
            TextOrCode(tx.Request.TargetWarehouseText, tx.Request.TargetWarehouse),
            tx.Request.TargetStorageBin ?? "",
            tx.Request.TargetBatch);
    }
}
