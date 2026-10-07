namespace Fam.Pulverentnahme.Web;

public sealed class StockRelocationService
{
    public const string Kind = "stock-relocation";

    private readonly SeparateOperationStore _store;
    private readonly MaterialTransferBookingService _booking;
    private readonly MachineTankService _tanks;
    private readonly SourceStockService _sources;
    private readonly TargetLocationLookupService _targets;
    private readonly PersonnelService _personnel;

    public StockRelocationService(
        SeparateOperationStore store,
        MaterialTransferBookingService booking,
        MachineTankService tanks,
        SourceStockService sources,
        TargetLocationLookupService targets,
        PersonnelService personnel)
    {
        _store = store;
        _booking = booking;
        _tanks = tanks;
        _sources = sources;
        _targets = targets;
        _personnel = personnel;
    }

    public Task<SeparateOperation?> GetAsync(string id, CancellationToken ct) =>
        _store.GetAsync(Kind, id, ct);

    public async Task<SeparateOperation> ExecuteAsync(StockRelocationRequest request, CancellationToken ct)
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
                    throw new ProcessConflictException("clientOperationId gehört bereits zu anderen Umlagerungsdaten.");
                return existing;
            }

            var tx = new SeparateOperation
            {
                Kind = Kind,
                ClientOperationId = request.ClientOperationId,
                RequestJson = json
            };
            await SaveEventAsync(tx, "CREATED", "Pulver-Umlagerung wurde als WebApp-Vorgang angelegt.", ct);

            try
            {
                await ValidatePersonnelAsync(request.PersonnelNo, request.PersonnelName, ct);

                if (await _tanks.IsAllowedAsync(request.SourceWarehouse, ct))
                    throw new ProcessConflictException("Tanklager dürfen nicht als Quelle der Lagerplatz-Umlagerung verwendet werden.");

                if (await _tanks.IsAllowedAsync(request.TargetWarehouse, ct))
                    throw new ProcessConflictException("Umlagerung in ein Tanklager ist nicht zulässig.");

                var sourcePositions = await _sources.ReadPositionsAsync(request.Article, request.SourceWarehouse, ct);
                var sourceMatches = sourcePositions.Where(x =>
                    string.Equals(x.Warehouse, request.SourceWarehouse, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.StorageBin, request.SourceStorageBin, StringComparison.Ordinal)
                    && string.Equals(x.Batch, request.Batch, StringComparison.Ordinal))
                    .ToList();

                if (sourceMatches.Count != 1)
                    throw new ProcessConflictException(
                        $"Die Quellposition {request.SourceWarehouse} / {request.SourceStorageBin}, Charge {request.Batch}, " +
                        "ist in Oxaion nicht mehr eindeutig mit positivem Bestand vorhanden. Lagerübersicht neu laden.");

                var current = sourceMatches[0];
                if (!string.Equals(current.Unit, "KGM", StringComparison.OrdinalIgnoreCase))
                    throw new ProcessConflictException($"Die Quellposition wird in der unerwarteten Einheit {current.Unit} geführt.");

                if (Math.Abs(current.QuantityKg - request.ExpectedSourceQuantityKg) >= 0.0005m)
                    throw new ProcessConflictException(
                        $"Der Quellbestand hat sich seit der Anzeige geändert. Angezeigt waren {request.ExpectedSourceQuantityKg:0.###} kg, " +
                        $"aktuell sind {current.QuantityKg:0.###} kg vorhanden. Lagerübersicht neu laden.");

                if (request.QuantityKg > current.QuantityKg + 0.0005m)
                    throw new ProcessConflictException(
                        $"Umlagerungsmenge {request.QuantityKg:0.###} kg ist größer als der aktuelle Quellbestand {current.QuantityKg:0.###} kg.");

                await ValidateTargetAsync(request, ct);

                var specs = BuildTransferSpecs(request);
                await _booking.BookAsync(
                    tx,
                    DateOnly.FromDateTime(DateTime.Today),
                    request.PersonnelNo,
                    request.PersonnelName,
                    "Pulver umlagern",
                    specs,
                    ct);
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
                    tx.Status = TransactionStatuses.ManualReviewRequired;
                    await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED",
                        $"Oxaion hat die Umlagerung nach Anlage des Belegs {tx.DocumentNo} abgelehnt: {ex.Code}. " +
                        "Der Buchungsausgang ist nicht sicher. Nicht erneut umlagern; zuerst 'Status in Oxaion prüfen' verwenden.", ct);
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
        finally
        {
            gate.Release();
        }
    }

    public async Task<SeparateOperation> ReconcileAsync(string id, CancellationToken ct)
    {
        var tx = await _store.GetAsync(Kind, id, ct) ?? throw new KeyNotFoundException();
        if (tx.Status == TransactionStatuses.Success) return tx;
        var request = tx.ReadRequest<StockRelocationRequest>();
        await _booking.ReconcileAsync(tx, BuildTransferSpecs(request), ct);
        return tx;
    }

    internal static IReadOnlyList<TransferSpec> BuildTransferSpecs(StockRelocationRequest request) =>
    [
        new TransferSpec(
            1,
            "LF",
            request.Article,
            request.ArticleText,
            request.SourceWarehouse,
            request.SourceWarehouseText,
            request.SourceStorageBin,
            request.Batch,
            request.TargetWarehouse,
            request.TargetWarehouse,
            request.TargetStorageBin,
            "",
            RoundKg(request.QuantityKg))
    ];

    internal static void Validate(StockRelocationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClientOperationId)
            || string.IsNullOrWhiteSpace(request.PersonnelNo)
            || string.IsNullOrWhiteSpace(request.PersonnelName)
            || string.IsNullOrWhiteSpace(request.Article)
            || string.IsNullOrWhiteSpace(request.SourceWarehouse)
            || string.IsNullOrWhiteSpace(request.SourceStorageBin)
            || string.IsNullOrWhiteSpace(request.Batch)
            || string.IsNullOrWhiteSpace(request.TargetWarehouse)
            || string.IsNullOrWhiteSpace(request.TargetStorageBin))
            throw new ArgumentException("Quelle, Charge, Ziellagerort, Ziellagerplatz und Mitarbeiter sind erforderlich.");

        if (!request.Article.StartsWith("RP.", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Nur RP.* Pulverartikel dürfen aus der Lagerübersicht umgelagert werden.");

        var expected = RoundKg(request.ExpectedSourceQuantityKg);
        var amount = RoundKg(request.QuantityKg);
        if (expected <= 0m) throw new ArgumentException("Angezeigter Quellbestand muss größer als 0 kg sein.");
        if (amount <= 0m) throw new ArgumentException("Umlagerungsmenge muss größer als 0 kg sein.");
        if (amount > expected + 0.0005m) throw new ArgumentException("Umlagerungsmenge darf den angezeigten Quellbestand nicht überschreiten.");

        if (string.Equals(request.SourceWarehouse, request.TargetWarehouse, StringComparison.OrdinalIgnoreCase)
            && string.Equals(request.SourceStorageBin, request.TargetStorageBin, StringComparison.Ordinal))
            throw new ArgumentException("Quelle und Ziel müssen unterschiedliche Lagerplätze sein.");
    }

    private async Task ValidateTargetAsync(StockRelocationRequest request, CancellationToken ct)
    {
        var warehouses = await _targets.SearchWarehousesAsync(request.TargetWarehouse, null, ct);
        var warehouseMatches = warehouses
            .Where(x => string.Equals(x.Warehouse, request.TargetWarehouse, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (warehouseMatches.Count != 1)
            throw new ProcessConflictException(
                $"Ziellagerort {request.TargetWarehouse} ist nicht eindeutig als lagerplatzgeführter Oxaion-Lagerort bestätigt.");

        var bins = await _targets.SearchStorageBinsAsync(request.TargetWarehouse, request.TargetStorageBin, ct);
        var binMatches = bins.Count(x =>
            string.Equals(x.StorageBin, request.TargetStorageBin, StringComparison.Ordinal));
        if (binMatches != 1)
            throw new ProcessConflictException(
                $"Ziellagerplatz {request.TargetWarehouse} / {request.TargetStorageBin} ist nicht eindeutig in Oxaion bestätigt.");
    }

    private async Task ValidatePersonnelAsync(string no, string name, CancellationToken ct)
    {
        var employee = await _personnel.ReadExactAsync(no, ct);
        if (employee is null || employee.FullName != name)
            throw new ProcessConflictException("Mitarbeiter ist in Oxaion nicht mehr eindeutig bestätigt.");
    }

    private static decimal RoundKg(decimal value) =>
        Math.Round(value, 3, MidpointRounding.AwayFromZero);

    private async Task SaveEventAsync(SeparateOperation tx, string stage, string message, CancellationToken ct)
    {
        tx.Stage = stage;
        tx.Message = message;
        tx.Events.Add(new TransactionEvent(DateTimeOffset.UtcNow, stage, message));
        await _store.SaveAsync(tx, ct);
    }
}
