using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace Fam.Pulverentnahme.Web;

public sealed class TankOutService
{
    public const string Kind = "tank-out";
    private readonly SeparateOperationStore _store;
    private readonly MaterialTransferBookingService _booking;
    private readonly MachineTankService _tanks;
    private readonly PersonnelService _personnel;
    private readonly OxaionClient _oxaion;
    private readonly InventoryCorrectionService _corrections;

    public TankOutService(
        SeparateOperationStore store,
        MaterialTransferBookingService booking,
        MachineTankService tanks,
        PersonnelService personnel,
        OxaionClient oxaion,
        InventoryCorrectionService corrections)
    {
        _store = store;
        _booking = booking;
        _tanks = tanks;
        _personnel = personnel;
        _oxaion = oxaion;
        _corrections = corrections;
    }

    public Task<SeparateOperation?> GetAsync(string id, CancellationToken ct) => _store.GetAsync(Kind, id, ct);

    public async Task<SeparateOperation> ExecuteAsync(TankOutRequest request, CancellationToken ct)
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
                if (existing.RequestJson != json) throw new ProcessConflictException("clientOperationId already belongs to different tank-out data.");
                return existing;
            }
            var tx = NewOperation(Kind, request.ClientOperationId, json);
            await SaveEventAsync(tx, "CREATED", "Tank-out transaction created.", ct);
            try
            {
                await ValidatePersonnelAsync(request.PersonnelNo, request.PersonnelName, ct);
                var stock = await _tanks.ReadStockAsync(request.TankWarehouse, ct);
                if (stock.Status != MachineStockStatuses.Unique || stock.Rows.Count != 1)
                    throw new ProcessConflictException(stock.Message);
                var current = stock.Rows[0];
                if (!StockEquals(current, request.Article, request.Batch, request.QuantityKg))
                    throw new ProcessConflictException($"Tank stock changed. Current: {current.Article}, {current.Batch}, {current.QuantityKg:0.###} kg.");

                var systemKg = RoundKg(request.QuantityKg);
                var weighedKg = RoundKg(request.WeighedQuantityKg ?? request.QuantityKg);
                var deltaKg = weighedKg - systemKg;

                if (Math.Abs(deltaKg) >= 0.0005m)
                {
                    var correctionKey = deltaKg > 0m ? "I1" : "I2";
                    var correctionKg = Math.Abs(deltaKg);
                    var childId = request.ClientOperationId + "-corr";
                    tx.RelatedOperationId = childId;
                    tx.Status = TransactionStatuses.Validating;
                    await SaveEventAsync(tx, "CORRECTION_STARTING",
                        $"Tank weighing differs from Oxaion by {deltaKg:0.###} kg. Starting {correctionKey} child operation {childId}.", ct);

                    var correction = await _corrections.ExecuteAsync(new InventoryCorrectionRequest(
                        childId,
                        request.PersonnelNo,
                        request.PersonnelName,
                        request.TankWarehouse,
                        request.TankWarehouseText,
                        request.Article,
                        request.ArticleText,
                        request.Batch,
                        systemKg,
                        correctionKey,
                        correctionKg), ct);

                    if (correction.Status != TransactionStatuses.Success)
                    {
                        tx.Status = correction.Status is TransactionStatuses.Uncertain or TransactionStatuses.ManualReviewRequired
                            ? TransactionStatuses.ManualReviewRequired
                            : correction.Status;
                        var correctionReason = string.IsNullOrWhiteSpace(correction.Message)
                            ? "Oxaion hat keinen eindeutigen erfolgreichen Korrekturstatus geliefert."
                            : correction.Message;
                        await SaveEventAsync(tx,
                            tx.Status == TransactionStatuses.ManualReviewRequired ? "MANUAL_REVIEW_REQUIRED" : tx.Status,
                            $"Bestandskorrektur {correctionKey} wurde nicht gebucht: {correctionReason} " +
                            $"(Teilvorgang {childId}: {correction.Status}/{correction.Stage}). " +
                            "Die LF/LE-Auslagerung wurde deshalb nicht gestartet.", ct);
                        return tx;
                    }

                    var corrected = await _tanks.ReadStockAsync(request.TankWarehouse, ct);
                    if (corrected.Status != MachineStockStatuses.Unique || corrected.Rows.Count != 1 ||
                        !StockEquals(corrected.Rows[0], request.Article, request.Batch, weighedKg))
                    {
                        tx.Status = TransactionStatuses.ManualReviewRequired;
                        await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED",
                            $"Korrektur {correction.DocumentNo} ist bestätigt, aber der Tankbestand entspricht danach nicht exakt der Waage " +
                            $"({weighedKg:0.###} kg). LF/LE wurde nicht gestartet. Nicht erneut korrigieren.", ct);
                        return tx;
                    }

                    await SaveEventAsync(tx, "CORRECTION_CONFIRMED",
                        $"Tankbestand wurde mit {correctionKey} auf die gewogenen {weighedKg:0.###} kg korrigiert und erneut bestätigt.", ct);
                }

                var targetText = await ResolveWarehouseTextAsync(request.TargetWarehouse, ct);
                var specs = new[]
                {
                    new TransferSpec(1, "LF", request.Article, request.ArticleText,
                        request.TankWarehouse, request.TankWarehouseText, "", request.Batch,
                        request.TargetWarehouse, targetText, request.TargetStorageBin, "", weighedKg)
                };
                await _booking.BookAsync(tx, DateOnly.FromDateTime(DateTime.Today), request.PersonnelNo, request.PersonnelName,
                    "Pulver aus Tank auf Lagerplatz", specs, ct);
            }
            catch (ProcessConflictException ex)
            {
                tx.Status = TransactionStatuses.Conflict;
                await SaveEventAsync(tx, "CONFLICT", ex.Message, ct);
            }
            catch (OxaionRejectedException ex)
            {
                if (string.IsNullOrWhiteSpace(tx.DocumentNo))
                {
                    tx.Status = TransactionStatuses.Rejected;
                    await SaveEventAsync(tx, "REJECTED", ex.Message, ct);
                }
                else
                {
                    // Once a material document number exists, a later FCOD is not enough evidence
                    // that no movement was persisted. Preserve the operation for reconciliation.
                    tx.Status = TransactionStatuses.ManualReviewRequired;
                    await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED",
                        $"Oxaion hat den Vorgang nach Anlage des Belegs {tx.DocumentNo} abgelehnt: {ex.Code}. " +
                        "Der Buchungsausgang wird deshalb nicht als sicher abgelehnt angenommen. Nicht erneut buchen; zuerst 'Status in Oxaion prüfen' verwenden.", ct);
                }
            }
            catch (OxaionTransportException ex)
            {
                tx.Status = TransactionStatuses.Uncertain;
                await SaveEventAsync(tx, "UNCERTAIN", ex.Message, ct);
            }
            catch (Exception ex)
            {
                tx.Status = TransactionStatuses.ManualReviewRequired;
                await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED", ex.Message, ct);
            }
            return tx;
        }
        finally { gate.Release(); }
    }

    public async Task<SeparateOperation> ReconcileAsync(string id, CancellationToken ct)
    {
        var tx = await _store.GetAsync(Kind, id, ct) ?? throw new KeyNotFoundException();
        if (tx.Status == TransactionStatuses.Success) return tx;
        var request = tx.ReadRequest<TankOutRequest>();
        if (string.IsNullOrWhiteSpace(tx.DocumentNo))
        {
            tx.Status = TransactionStatuses.ManualReviewRequired;
            await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED",
                "Für den Tank-Auslagerungsvorgang liegt noch kein LF/LE-Beleg vor. " +
                "Falls zuvor eine I1/I2-Korrektur erfolgt ist, diese Teiltransaktion zuerst prüfen; die App startet LF/LE bei der Statusprüfung nicht automatisch.", ct);
            return tx;
        }

        var targetText = await ResolveWarehouseTextAsync(request.TargetWarehouse, ct);
        var weighedKg = RoundKg(request.WeighedQuantityKg ?? request.QuantityKg);
        var specs = new[]
        {
            new TransferSpec(1, "LF", request.Article, request.ArticleText,
                request.TankWarehouse, request.TankWarehouseText, "", request.Batch,
                request.TargetWarehouse, targetText, request.TargetStorageBin, "", weighedKg)
        };
        await _booking.ReconcileAsync(tx, specs, ct);
        return tx;
    }

    private async Task<string> ResolveWarehouseTextAsync(string warehouse, CancellationToken ct)
    {
        await using var session = await _oxaion.ConnectAsync(ct);
        var plain = await session.CallAsync("US00006J", "*GETPLAIN", new Dictionary<string, string>
        {
            ["MFLD"] = "LAGO", ["PGMN"] = "US30600J", ["LAGO"] = warehouse,
            ["PFIELD"] = "TX_LAGO", ["FIELD"] = "LAGO"
        }, ct);
        OxaionSession.AssertNoFcod(plain);
        return plain.Dta.TryGetValue("TX_LAGO", out var text) && !string.IsNullOrWhiteSpace(text) ? text : warehouse;
    }

    private async Task ValidatePersonnelAsync(string no, string name, CancellationToken ct)
    {
        var employee = await _personnel.ReadExactAsync(no, ct);
        if (employee is null || employee.FullName != name) throw new ProcessConflictException("Mitarbeiter ist in Oxaion nicht mehr eindeutig bestätigt.");
    }

    private static void Validate(TankOutRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.ClientOperationId) || string.IsNullOrWhiteSpace(r.PersonnelNo) || string.IsNullOrWhiteSpace(r.Article)
            || string.IsNullOrWhiteSpace(r.TankWarehouse) || string.IsNullOrWhiteSpace(r.Batch)
            || string.IsNullOrWhiteSpace(r.TargetWarehouse) || string.IsNullOrWhiteSpace(r.TargetStorageBin))
            throw new ArgumentException("Tank, article/batch, target warehouse/storage bin and personnel are required.");
        if (r.QuantityKg <= 0) throw new ArgumentException("Tank quantity must be > 0.");
        var weighed = RoundKg(r.WeighedQuantityKg ?? r.QuantityKg);
        if (weighed <= 0m) throw new ArgumentException("Gewogene Auslagerungsmenge muss > 0 sein.");
        if (string.Equals(r.TankWarehouse, r.TargetWarehouse, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Target warehouse must differ from machine tank warehouse.");
    }

    private static bool StockEquals(MachineStockRow row, string article, string batch, decimal qty) =>
        row.Article.Equals(article, StringComparison.OrdinalIgnoreCase) && row.Batch == batch && Math.Abs(row.QuantityKg - qty) < 0.0005m;

    internal static decimal RoundKg(decimal value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
    private static SeparateOperation NewOperation(string kind, string id, string json) => new() { Kind = kind, ClientOperationId = id, RequestJson = json };
    private async Task SaveEventAsync(SeparateOperation tx, string stage, string message, CancellationToken ct)
    { tx.Stage = stage; tx.Message = message; tx.Events.Add(new TransactionEvent(DateTimeOffset.UtcNow, stage, message)); await _store.SaveAsync(tx, ct); }
}
