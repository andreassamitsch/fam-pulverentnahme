using System.Globalization;

namespace Fam.Pulverentnahme.Web;

public sealed record InventoryCorrectionRequest(
    string ClientOperationId,
    string PersonnelNo,
    string PersonnelName,
    string TankWarehouse,
    string TankWarehouseText,
    string Article,
    string ArticleText,
    string Batch,
    decimal ExpectedQuantityKg,
    string BookingKey,
    decimal CorrectionQuantityKg) : ISeparatePersonnelRequest;

public sealed class InventoryCorrectionService
{
    public const string Kind = "inventory-correction";
    private readonly SeparateOperationStore _store;
    private readonly MaterialTransferBookingService _booking;
    private readonly MachineTankService _tanks;
    private readonly PersonnelService _personnel;

    public InventoryCorrectionService(
        SeparateOperationStore store,
        MaterialTransferBookingService booking,
        MachineTankService tanks,
        PersonnelService personnel)
    {
        _store = store;
        _booking = booking;
        _tanks = tanks;
        _personnel = personnel;
    }

    public Task<SeparateOperation?> GetAsync(string id, CancellationToken ct) => _store.GetAsync(Kind, id, ct);

    public async Task<SeparateOperation> ExecuteAsync(InventoryCorrectionRequest request, CancellationToken ct)
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
                if (existing.RequestJson != json)
                    throw new ProcessConflictException("clientOperationId already belongs to different inventory-correction data.");
                return existing;
            }

            var tx = new SeparateOperation
            {
                Kind = Kind,
                ClientOperationId = request.ClientOperationId,
                RequestJson = json
            };
            await SaveEventAsync(tx, "CREATED", $"{request.BookingKey} inventory correction created.", ct);

            try
            {
                var employee = await _personnel.ReadExactAsync(request.PersonnelNo, ct);
                if (employee is null || employee.FullName != request.PersonnelName)
                    throw new ProcessConflictException("Mitarbeiter ist in Oxaion nicht mehr eindeutig bestätigt.");

                var stock = await _tanks.ReadStockAsync(request.TankWarehouse, ct);
                if (stock.Status != MachineStockStatuses.Unique || stock.Rows.Count != 1)
                    throw new ProcessConflictException(stock.Message);
                var row = stock.Rows[0];
                if (!SameStock(row, request.Article, request.Batch, request.ExpectedQuantityKg))
                    throw new ProcessConflictException(
                        $"Tankbestand hat sich vor der {request.BookingKey}-Korrektur geändert. " +
                        $"Aktuell {row.Article}/{row.Batch}/{row.QuantityKg:0.###} kg.");

                await _booking.BookInventoryCorrectionAsync(
                    tx,
                    DateOnly.FromDateTime(DateTime.Today),
                    request.PersonnelNo,
                    request.PersonnelName,
                    request.BookingKey == "I2" ? "Tankwiegung Schwund" : "Tankwiegung Mehrbestand",
                    request.BookingKey,
                    request.Article,
                    request.ArticleText,
                    request.TankWarehouse,
                    request.TankWarehouseText,
                    request.Batch,
                    request.CorrectionQuantityKg,
                    ct);

                if (tx.Status != TransactionStatuses.Success)
                    return tx;

                var expectedAfter = request.ExpectedQuantityKg +
                    (request.BookingKey == "I1" ? request.CorrectionQuantityKg : -request.CorrectionQuantityKg);
                var after = await _tanks.ReadStockAsync(request.TankWarehouse, ct);
                if (expectedAfter > 0.0005m)
                {
                    if (after.Status != MachineStockStatuses.Unique || after.Rows.Count != 1 ||
                        !SameStock(after.Rows[0], request.Article, request.Batch, expectedAfter))
                    {
                        tx.Status = TransactionStatuses.ManualReviewRequired;
                        await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED",
                            $"Oxaion-Beleg {tx.DocumentNo} enthält die Korrektur, aber der erwartete Tankbestand " +
                            $"{expectedAfter:0.###} kg konnte danach nicht eindeutig bestätigt werden. Nicht erneut korrigieren.", ct);
                    }
                }
                else if (after.Status != MachineStockStatuses.Empty)
                {
                    tx.Status = TransactionStatuses.ManualReviewRequired;
                    await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED",
                        $"Oxaion-Beleg {tx.DocumentNo} enthält die Korrektur, aber der Tank ist danach nicht eindeutig leer. Nicht erneut korrigieren.", ct);
                }
            }
            catch (ProcessConflictException ex)
            {
                tx.Status = TransactionStatuses.Conflict;
                await SaveEventAsync(tx, "CONFLICT", ex.Message, ct);
            }
            catch (OxaionRejectedException ex)
            {
                tx.Status = string.IsNullOrWhiteSpace(tx.DocumentNo)
                    ? TransactionStatuses.Rejected
                    : TransactionStatuses.ManualReviewRequired;
                await SaveEventAsync(tx, tx.Status, ex.Message, ct);
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
        finally
        {
            gate.Release();
        }
    }

    public async Task<SeparateOperation> ReconcileAsync(string id, CancellationToken ct)
    {
        var tx = await _store.GetAsync(Kind, id, ct) ?? throw new KeyNotFoundException();
        if (tx.Status == TransactionStatuses.Success) return tx;
        var request = tx.ReadRequest<InventoryCorrectionRequest>();

        if (!string.IsNullOrWhiteSpace(tx.DocumentNo))
            await _booking.ReconcileInventoryCorrectionAsync(
                tx, request.BookingKey, request.Article, request.TankWarehouse, request.Batch,
                request.CorrectionQuantityKg, ct);

        if (tx.Status == TransactionStatuses.Success)
        {
            var expectedAfter = request.ExpectedQuantityKg +
                (request.BookingKey == "I1" ? request.CorrectionQuantityKg : -request.CorrectionQuantityKg);
            var after = await _tanks.ReadStockAsync(request.TankWarehouse, ct);
            var stateMatches = expectedAfter <= 0.0005m
                ? after.Status == MachineStockStatuses.Empty
                : after.Status == MachineStockStatuses.Unique && after.Rows.Count == 1 &&
                  SameStock(after.Rows[0], request.Article, request.Batch, expectedAfter);
            if (!stateMatches)
            {
                tx.Status = TransactionStatuses.ManualReviewRequired;
                await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED",
                    "Korrekturbewegung ist im Oxaion-Beleg vorhanden, der aktuelle Tankzustand beweist den erwarteten Endbestand aber nicht. Nicht erneut korrigieren.", ct);
            }
        }
        return tx;
    }

    private static bool SameStock(MachineStockRow row, string article, string batch, decimal qty) =>
        row.Article.Equals(article, StringComparison.OrdinalIgnoreCase) &&
        row.Batch == batch &&
        Math.Abs(row.QuantityKg - qty) < 0.0005m;

    private static void Validate(InventoryCorrectionRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.ClientOperationId) || string.IsNullOrWhiteSpace(r.PersonnelNo) ||
            string.IsNullOrWhiteSpace(r.TankWarehouse) || string.IsNullOrWhiteSpace(r.Article) ||
            string.IsNullOrWhiteSpace(r.Batch))
            throw new ArgumentException("Operation, personnel, tank, article and batch are required.");
        if (r.ExpectedQuantityKg <= 0m || r.CorrectionQuantityKg <= 0m)
            throw new ArgumentException("Expected tank quantity and correction quantity must be > 0.");
        if (r.BookingKey is not ("I1" or "I2"))
            throw new ArgumentException("Only the confirmed correction booking keys I1 and I2 are allowed.");
        if (r.BookingKey == "I2" && r.CorrectionQuantityKg > r.ExpectedQuantityKg + 0.0005m)
            throw new ArgumentException("Schwund darf den bestätigten Tankbestand nicht überschreiten.");
    }

    private async Task SaveEventAsync(SeparateOperation tx, string stage, string message, CancellationToken ct)
    {
        tx.Stage = stage;
        tx.Message = message;
        tx.Events.Add(new TransactionEvent(DateTimeOffset.UtcNow, stage, message));
        await _store.SaveAsync(tx, ct);
    }
}
