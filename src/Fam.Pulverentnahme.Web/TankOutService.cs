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

    public TankOutService(SeparateOperationStore store, MaterialTransferBookingService booking, MachineTankService tanks, PersonnelService personnel, OxaionClient oxaion)
    {
        _store = store; _booking = booking; _tanks = tanks; _personnel = personnel; _oxaion = oxaion;
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

                var targetText = await ResolveWarehouseTextAsync(request.TargetWarehouse, ct);
                var specs = new[]
                {
                    new TransferSpec(1, "LF", request.Article, request.ArticleText,
                        request.TankWarehouse, request.TankWarehouseText, "", request.Batch,
                        request.TargetWarehouse, targetText, request.TargetStorageBin, "", request.QuantityKg)
                };
                await _booking.BookAsync(tx, DateOnly.FromDateTime(DateTime.Today), request.PersonnelNo, request.PersonnelName,
                    "Pulver aus Tank auf Lagerplatz", specs, ct);
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
        var request = tx.ReadRequest<TankOutRequest>();
        var targetText = await ResolveWarehouseTextAsync(request.TargetWarehouse, ct);
        var specs = new[]
        {
            new TransferSpec(1, "LF", request.Article, request.ArticleText,
                request.TankWarehouse, request.TankWarehouseText, "", request.Batch,
                request.TargetWarehouse, targetText, request.TargetStorageBin, "", request.QuantityKg)
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
        if (string.Equals(r.TankWarehouse, r.TargetWarehouse, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Target warehouse must differ from machine tank warehouse.");
    }

    private static bool StockEquals(MachineStockRow row, string article, string batch, decimal qty) =>
        row.Article.Equals(article, StringComparison.OrdinalIgnoreCase) && row.Batch == batch && Math.Abs(row.QuantityKg - qty) < 0.0005m;
    private static SeparateOperation NewOperation(string kind, string id, string json) => new() { Kind = kind, ClientOperationId = id, RequestJson = json };
    private async Task SaveEventAsync(SeparateOperation tx, string stage, string message, CancellationToken ct)
    { tx.Stage = stage; tx.Message = message; tx.Events.Add(new TransactionEvent(DateTimeOffset.UtcNow, stage, message)); await _store.SaveAsync(tx, ct); }
}
