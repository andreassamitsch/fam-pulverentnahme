using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace Fam.Pulverentnahme.Web;

public sealed class FillNewService
{
    public const string Kind = "fill-new";
    private readonly SeparateOperationStore _store;
    private readonly MaterialTransferBookingService _booking;
    private readonly MachineTankService _tanks;
    private readonly SourceStockService _sources;
    private readonly PersonnelService _personnel;

    public FillNewService(SeparateOperationStore store, MaterialTransferBookingService booking, MachineTankService tanks, SourceStockService sources, PersonnelService personnel)
    { _store = store; _booking = booking; _tanks = tanks; _sources = sources; _personnel = personnel; }

    public Task<SeparateOperation?> GetAsync(string id, CancellationToken ct) => _store.GetAsync(Kind, id, ct);

    public async Task<SeparateOperation> ExecuteAsync(FillNewRequest request, CancellationToken ct)
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
                if (existing.RequestJson != json) throw new ProcessConflictException("clientOperationId already belongs to different fill-new data.");
                return existing;
            }
            var tx = new SeparateOperation { Kind = Kind, ClientOperationId = request.ClientOperationId, RequestJson = json };
            await SaveEventAsync(tx, "CREATED", "Fill-new transaction created.", ct);
            try
            {
                var employee = await _personnel.ReadExactAsync(request.PersonnelNo, ct);
                if (employee is null || employee.FullName != request.PersonnelName)
                    throw new ProcessConflictException("Mitarbeiter ist in Oxaion nicht mehr eindeutig bestätigt.");
                var tank = await _tanks.ReadStockAsync(request.TankWarehouse, ct);
                if (tank.Status != MachineStockStatuses.Empty)
                    throw new ProcessConflictException("Maschinentank ist vor der Neubefüllung nicht mehr eindeutig leer. " + tank.Message);
                var validation = await _sources.ValidateSourcesAsync(request.Article, request.Sources, ct);
                if (!validation.IsValid) throw new ProcessConflictException(validation.Message);

                var specs = request.Sources.Select((s, i) => new TransferSpec(
                    i + 1, "LM", request.Article, request.ArticleText,
                    s.Warehouse, s.WarehouseText, s.StorageBin ?? "", s.Batch,
                    request.TankWarehouse, request.TankWarehouseText, "", request.TargetBatch,
                    s.AmountKg, i == 0 ? request.ProductionDate : null)).ToArray();
                await _booking.BookAsync(tx, request.BookingDate, request.PersonnelNo, request.PersonnelName,
                    "Pulver in Maschinentank", specs, ct);
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
        var request = tx.ReadRequest<FillNewRequest>();
        var specs = request.Sources.Select((s, i) => new TransferSpec(
            i + 1, "LM", request.Article, request.ArticleText,
            s.Warehouse, s.WarehouseText, s.StorageBin ?? "", s.Batch,
            request.TankWarehouse, request.TankWarehouseText, "", request.TargetBatch,
            s.AmountKg, i == 0 ? request.ProductionDate : null)).ToArray();
        await _booking.ReconcileAsync(tx, specs, ct);
        return tx;
    }

    private static void Validate(FillNewRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.ClientOperationId) || string.IsNullOrWhiteSpace(r.PersonnelNo) || string.IsNullOrWhiteSpace(r.Article)
            || string.IsNullOrWhiteSpace(r.TankWarehouse) || string.IsNullOrWhiteSpace(r.TargetBatch))
            throw new ArgumentException("Operation, personnel, tank, article and target MIX batch are required.");
        if (r.Sources is null || r.Sources.Count == 0) throw new ArgumentException("At least one powder source is required.");
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (r.BookingDate != today || r.ProductionDate != today) throw new ArgumentException("Booking and MIX production date must be today.");
        if (!ReplenishmentRules.IsValidGeneratedMixBatch(r.Article, r.TargetBatch, r.ProductionDate))
            throw new ArgumentException("Target MIX batch does not match the confirmed generated MIX schema.");
        foreach (var s in r.Sources)
        {
            if (s.AmountKg <= 0 || string.IsNullOrWhiteSpace(s.Warehouse) || string.IsNullOrWhiteSpace(s.Batch))
                throw new ArgumentException("Every source requires warehouse, batch and amount > 0.");
            if (string.Equals(s.Warehouse, r.TankWarehouse, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Machine tank cannot be a fill source.");
            if (string.Equals(s.Batch, r.TargetBatch, StringComparison.Ordinal))
                throw new ArgumentException("A new MIX batch must always differ from every source batch.");
        }
    }

    private async Task SaveEventAsync(SeparateOperation tx, string stage, string message, CancellationToken ct)
    { tx.Stage = stage; tx.Message = message; tx.Events.Add(new TransactionEvent(DateTimeOffset.UtcNow, stage, message)); await _store.SaveAsync(tx, ct); }
}
