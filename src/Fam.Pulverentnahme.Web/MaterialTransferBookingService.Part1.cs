using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace Fam.Pulverentnahme.Web;

public sealed partial class MaterialTransferBookingService
{
    private readonly OxaionClient _oxaion;
    private readonly OxaionOptions _options;
    private readonly SeparateOperationStore _store;

    public MaterialTransferBookingService(
        OxaionClient oxaion,
        IOptions<OxaionOptions> options,
        SeparateOperationStore store)
    {
        _oxaion = oxaion;
        _options = options.Value;
        _store = store;
    }

    public async Task BookAsync(
        SeparateOperation tx,
        DateOnly bookingDate,
        string personnelNo,
        string personnelName,
        string bookingText,
        IReadOnlyList<TransferSpec> specs,
        CancellationToken ct)
    {
        if (specs.Count == 0) throw new ArgumentException("At least one transfer position is required.");
        await using var session = await _oxaion.ConnectAsync(ct);
        try
        {
            var header = await NewHeaderAsync(session, ct);
            await SaveEventAsync(tx, "HEADER_SUBMITTING", "Creating Oxaion material document header.", ct);
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
            if (string.IsNullOrWhiteSpace(doc)) throw new InvalidOperationException("LB20100J *PUTNEW did not return KOBGNR.");
            tx.DocumentNo = doc;
            tx.HeaderDta = put.Dta;
            tx.Status = TransactionStatuses.SendingToOxaion;
            await SaveEventAsync(tx, "HEADER_CREATED", $"Oxaion material document {doc} created.", ct);

            var context = await OpenExistingDocumentAsync(session, tx, ct);
            Dictionary<string, string> previous = tx.HeaderDta;
            for (var i = 0; i < specs.Count; i++)
            {
                var spec = specs[i];

                // A first LF position on an empty material document is not a normal continuation
                // position. The successful 2026-09-08 JET trace requires LB20115J *LOAD plus the
                // dedicated source/target validation replay. Using generic *NEW here reproduced
                // BEL1422 after the header had already been created.
                var validated = i == 0
                    && spec.Position == 1
                    && string.Equals(spec.BookingKey, "LF", StringComparison.Ordinal)
                    ? await AddFirstLfPositionAsync(session, tx, context.Ssid, bookingDate, op, spec, ct)
                    : await AddPositionAsync(session, tx, context.Ssid, previous, bookingDate, op, spec, ct);

                if (i + 1 < specs.Count)
                {
                    var refreshed = await ReadDocumentListAsync(session, context.Ssid, ct);
                    previous = TargetAccessStateFromRows(spec, refreshed.Xml, validated);
                }
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
                        "LB20100J", "*END", Dict(("KOBGNR", tx.DocumentNo!), ("KEYTYPE", "LKOPF")), CancellationToken.None);
                }
                catch
                {
                    // Cleanup is best-effort only. The original exception/status remains authoritative.
                }
            }
        }
    }

    public async Task<bool> ReconcileAsync(SeparateOperation tx, IReadOnlyList<TransferSpec> specs, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tx.DocumentNo) || tx.HeaderDta is null) return false;
        await using var session = await _oxaion.ConnectAsync(ct);
        try
        {
            var context = await OpenExistingDocumentAsync(session, tx, ct);
            var rows = MixBookingService.ParseMovements(context.List.Xml);
            tx.LastMovements = rows.ToList();
            if (!MovementsComplete(specs, rows, out var message))
            {
                tx.Status = TransactionStatuses.ManualReviewRequired;
                await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED", message, ct);
                try
                {
                    OxaionSession.AssertNoFcod(await session.CallAsync(
                        "LB20100J", "*END", Dict(("KOBGNR", tx.DocumentNo!), ("KEYTYPE", "LKOPF")), CancellationToken.None));
                }
                catch { }
                return false;
            }

            OxaionSession.AssertNoFcod(await session.CallAsync(
                "LB20100J", "*END", Dict(("KOBGNR", tx.DocumentNo!), ("KEYTYPE", "LKOPF")), CancellationToken.None));
            tx.Status = TransactionStatuses.Success;
            await SaveEventAsync(tx, "SUCCESS", "Oxaion material movements were found exactly once and the verification view was closed.", CancellationToken.None);
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

    internal static bool MovementsComplete(IReadOnlyList<TransferSpec> specs, IReadOnlyList<MovementRow> rows, out string message)
    {
        var expected = BuildExpected(specs);
        if (rows.Count != expected.Count)
        {
            message = $"Expected {expected.Count} movement rows, found {rows.Count}. No automatic retry is allowed.";
            return false;
        }
        foreach (var e in expected)
        {
            var count = rows.Count(r =>
                r.Position == e.Position && r.BookingKey == e.BookingKey && r.Article == e.Article &&
                r.Batch == e.Batch && r.Warehouse == e.Warehouse &&
                (r.StorageBin ?? "") == (e.StorageBin ?? "") && Math.Abs(r.Quantity - e.QuantityKg) < 0.0005m);
            if (count != 1)
            {
                message = $"Expected movement {e.Position}/{e.BookingKey}/{e.Warehouse}/{e.StorageBin}/{e.Batch}/{e.QuantityKg:0.###} kg was found {count} times.";
                return false;
            }
        }
        message = "All expected movements are present exactly once.";
        return true;
    }

    private async Task VerifyAndCloseAsync(OxaionSession session, SeparateOperation tx, IReadOnlyList<TransferSpec> specs, CancellationToken ct)
    {
        await SaveEventAsync(tx, "VERIFYING", "Reopening Oxaion material document for exact movement verification.", ct);
        var context = await OpenExistingDocumentAsync(session, tx, ct);
        Exception? verificationFailure = null;
        try
        {
            var rows = MixBookingService.ParseMovements(context.List.Xml);
            tx.LastMovements = rows.ToList();
            if (!MovementsComplete(specs, rows, out var message))
                verificationFailure = new InvalidOperationException("Final verification failed: " + message);
        }
        catch (Exception ex)
        {
            verificationFailure = ex;
        }

        // OPEN for final verification holds the material document. Always close the verification
        // view, including when the movement comparison itself failed. A disconnect is not a
        // substitute for LB20100J *END.
        try
        {
            var end = await session.CallAsync(
                "LB20100J", "*END", Dict(("KOBGNR", tx.DocumentNo!), ("KEYTYPE", "LKOPF")), CancellationToken.None);
            OxaionSession.AssertNoFcod(end);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Oxaion document {tx.DocumentNo} may already contain material movements, but the verification view could not be closed safely. Do not rebook; check the Oxaion lock and movements manually.", ex);
        }

        if (verificationFailure is not null) throw verificationFailure;

        tx.Status = TransactionStatuses.Success;
        await SaveEventAsync(tx, "SUCCESS", $"Oxaion document {tx.DocumentNo} verified with exactly {tx.LastMovements.Count} expected movements and explicitly closed.", CancellationToken.None);
    }

}
