namespace Fam.Pulverentnahme.Web;

public sealed class FillNewService
{
    public const string Kind = "fill-new";
    private readonly SeparateOperationStore _store;
    private readonly MaterialTransferBookingService _booking;
    private readonly MachineTankService _tanks;
    private readonly SourceStockService _sources;
    private readonly PersonnelService _personnel;

    public FillNewService(
        SeparateOperationStore store,
        MaterialTransferBookingService booking,
        MachineTankService tanks,
        SourceStockService sources,
        PersonnelService personnel)
    {
        _store = store;
        _booking = booking;
        _tanks = tanks;
        _sources = sources;
        _personnel = personnel;
    }

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
                if (existing.RequestJson != json)
                    throw new ProcessConflictException("clientOperationId already belongs to different fill-new data.");
                return existing;
            }

            var tx = new SeparateOperation
            {
                Kind = Kind,
                ClientOperationId = request.ClientOperationId,
                RequestJson = json
            };
            await SaveEventAsync(tx, "CREATED", "Neue Tankbefuellung wurde als WebApp-Vorgang angelegt.", ct);

            try
            {
                var employee = await _personnel.ReadExactAsync(request.PersonnelNo, ct);
                if (employee is null || employee.FullName != request.PersonnelName)
                    throw new ProcessConflictException("Mitarbeiter ist in Oxaion nicht mehr eindeutig bestätigt.");

                var tank = await _tanks.ReadStockAsync(request.TankWarehouse, ct);
                if (tank.Status != MachineStockStatuses.Empty)
                    throw new ProcessConflictException(
                        "Maschinentank ist vor der Neubefüllung nicht mehr eindeutig leer. " + tank.Message);

                var validation = await _sources.ValidateSourcesAsync(request.Article, request.Sources, ct);
                if (!validation.IsValid)
                    throw new ProcessConflictException(validation.Message);

                var specs = BuildTransferSpecs(request);
                await _booking.BookFillNewAsync(
                    tx,
                    request.BookingDate,
                    request.PersonnelNo,
                    request.PersonnelName,
                    "Pulver in Maschinentank",
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
                    // A later FCOD after KOBGNR exists cannot prove that no material movement was
                    // persisted. Preserve the operation and force read-only reconciliation.
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
        finally
        {
            gate.Release();
        }
    }

    public async Task<SeparateOperation> ReconcileAsync(string id, CancellationToken ct)
    {
        var tx = await _store.GetAsync(Kind, id, ct) ?? throw new KeyNotFoundException();
        if (tx.Status == TransactionStatuses.Success) return tx;

        var request = tx.ReadRequest<FillNewRequest>();
        var specs = BuildTransferSpecs(request);
        await _booking.ReconcileAsync(tx, specs, ct);
        return tx;
    }

    /// <summary>
    /// A stored MIX batch already represents a mixed powder identity. If it is the only source
    /// of an empty-tank fill, the confirmed LF -> LE transfer preserves that batch and no
    /// additional LM -> LN rebatch is required. As soon as any second source is part of the
    /// final request, a new generated MIX is required again.
    /// </summary>
    internal static bool PreserveSingleStoredMix(FillNewRequest request) =>
        request.Sources is { Count: 1 }
        && IsStoredMixBatch(request.Article, request.Sources[0].Batch);

    internal static bool IsStoredMixBatch(string article, string batch)
    {
        var prefix = ReplenishmentRules.BatchArticlePrefix(article) + "MIX_";
        return prefix.Length > 4
            && !string.IsNullOrWhiteSpace(batch)
            && batch.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && batch.Length > prefix.Length;
    }

    // Confirmed building blocks:
    // 1) empty warehouse target is filled with LF -> LE while preserving the source batch;
    // 2) an existing tank batch can be converted into a newly generated MIX with LM -> LN;
    // 3) further external batches can be added to the same MIX with LM -> LN.
    // Business decision 2026-09-09: one already stored MIX source alone keeps its batch. All other
    // final source sets create a new MIX. The decision is derived from the final request so adding
    // and subsequently removing a second source cannot leave stale MIX logic behind.
    internal static IReadOnlyList<TransferSpec> BuildTransferSpecs(FillNewRequest request)
    {
        if (request.Sources is null || request.Sources.Count == 0)
            throw new ArgumentException("At least one powder source is required.");

        var result = new List<TransferSpec>();
        var first = request.Sources[0];

        // Position 1: physically move the first real source into the empty tank. The successful
        // Oxaion trace proves LF -> LE and proves that this step preserves the source batch.
        result.Add(new TransferSpec(
            1,
            "LF",
            request.Article,
            request.ArticleText,
            first.Warehouse,
            first.WarehouseText,
            first.StorageBin ?? "",
            first.Batch,
            request.TankWarehouse,
            request.TankWarehouseText,
            "",
            "",
            first.AmountKg));

        // If this is exactly one already stored MIX source, preserving the LF/LE batch is the
        // complete intended result. No new MIX is written to Oxaion.
        if (PreserveSingleStoredMix(request))
            return result;

        // Otherwise convert the first tank batch into the newly generated MIX.
        result.Add(new TransferSpec(
            2,
            "LM",
            request.Article,
            request.ArticleText,
            request.TankWarehouse,
            request.TankWarehouseText,
            "",
            first.Batch,
            request.TankWarehouse,
            request.TankWarehouseText,
            "",
            request.TargetBatch,
            first.AmountKg,
            request.ProductionDate));

        // Position 3+: every additional external source joins the same new MIX.
        for (var i = 1; i < request.Sources.Count; i++)
        {
            var source = request.Sources[i];
            result.Add(new TransferSpec(
                i + 2,
                "LM",
                request.Article,
                request.ArticleText,
                source.Warehouse,
                source.WarehouseText,
                source.StorageBin ?? "",
                source.Batch,
                request.TankWarehouse,
                request.TankWarehouseText,
                "",
                request.TargetBatch,
                source.AmountKg));
        }

        return result;
    }

    private static void Validate(FillNewRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.ClientOperationId)
            || string.IsNullOrWhiteSpace(r.PersonnelNo)
            || string.IsNullOrWhiteSpace(r.Article)
            || string.IsNullOrWhiteSpace(r.TankWarehouse))
            throw new ArgumentException("Operation, personnel, tank and article are required.");

        if (r.Sources is null || r.Sources.Count == 0)
            throw new ArgumentException("At least one powder source is required.");

        var today = DateOnly.FromDateTime(DateTime.Today);
        if (r.BookingDate != today || r.ProductionDate != today)
            throw new ArgumentException("Booking and MIX production date must be today.");

        var preserveSingleMix = PreserveSingleStoredMix(r);
        if (!preserveSingleMix)
        {
            if (string.IsNullOrWhiteSpace(r.TargetBatch))
                throw new ArgumentException("A target MIX batch is required for this source combination.");
            if (!ReplenishmentRules.IsValidGeneratedMixBatch(r.Article, r.TargetBatch, r.ProductionDate))
                throw new ArgumentException("Target MIX batch does not match the confirmed generated MIX schema.");
        }

        foreach (var s in r.Sources)
        {
            if (s.AmountKg <= 0 || string.IsNullOrWhiteSpace(s.Warehouse) || string.IsNullOrWhiteSpace(s.Batch))
                throw new ArgumentException("Every source requires warehouse, batch and amount > 0.");
            if (string.Equals(s.Warehouse, r.TankWarehouse, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Machine tank cannot be a fill source.");
            if (!preserveSingleMix && string.Equals(s.Batch, r.TargetBatch, StringComparison.Ordinal))
                throw new ArgumentException("A new MIX batch must differ from every source batch.");
        }
    }

    private async Task SaveEventAsync(SeparateOperation tx, string stage, string message, CancellationToken ct)
    {
        tx.Stage = stage;
        tx.Message = message;
        tx.Events.Add(new TransactionEvent(DateTimeOffset.UtcNow, stage, message));
        await _store.SaveAsync(tx, ct);
    }
}
