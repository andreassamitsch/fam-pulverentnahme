namespace Fam.Pulverentnahme.Web;

/// <summary>
/// Read-only overview for the operator inventory page.
///
/// The general RP.*/PB.* stock SQL is the leading source for this information-only view. Machine tank
/// warehouses are still defined dynamically by Oxaion ULGSTP / LGLGART=02, but their displayed
/// stock is derived from the same SQL result instead of opening one serial LB30230R HTTP session
/// per tank. Productive tank/booking processes continue to use their confirmed Oxaion HTTP paths.
///
/// Recognition colours remain presentation aids only and are loaded once per distinct article with
/// bounded parallelism so a larger powder portfolio does not turn into a long serial wait.
/// </summary>
public sealed class InventoryOverviewService
{
    private const int MaxColorConcurrency = 3;

    private readonly InventoryService _inventory;
    private readonly MachineTankService _machineTanks;
    private readonly OxaionClient _oxaion;

    public InventoryOverviewService(
        InventoryService inventory,
        MachineTankService machineTanks,
        OxaionClient oxaion)
    {
        _inventory = inventory;
        _machineTanks = machineTanks;
        _oxaion = oxaion;
    }

    public async Task<InventoryOverviewResult> ReadAsync(CancellationToken ct)
    {
        // Both are read-only SQL queries against the same active Oxaion environment and can safely
        // overlap. This also guarantees that STAGING/PRODUCTION selection applies consistently.
        var stockTask = _inventory.ReadPowderStockAsync(ct);
        var optionsTask = _machineTanks.ReadOptionsAsync(ct);

        var stock = await stockTask;

        IReadOnlyList<MachineTankOption> options = [];
        string? tankLookupError = null;
        try
        {
            options = await optionsTask;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            tankLookupError = ex.Message;
        }

        var tankWarehouses = options
            .Select(x => x.Warehouse)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var tanks = options
            .Select(option => BuildTankOverview(option, stock))
            .ToList();

        if (tankLookupError is not null)
        {
            tanks.Add(new InventoryTankOverview(
                "", "", "UNAVAILABLE",
                $"Maschinentanks konnten nicht gelesen werden: {tankLookupError}",
                [], null));
        }

        // The lower "Pulverlager" section must not repeat positions already presented as
        // machine tanks in the dedicated upper section.
        var powderStock = stock
            .Where(x => !tankWarehouses.Contains(x.Warehouse))
            .ToList();

        var allArticles = powderStock.Select(x => x.Article)
            .Concat(tanks.SelectMany(x => x.Rows).Select(x => x.Article))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var colors = await ReadColorsAsync(allArticles, ct);

        tanks = ApplyRecognitionColors(tanks, colors);

        return new InventoryOverviewResult(powderStock, tanks, colors);
    }

    internal static List<InventoryTankOverview> ApplyRecognitionColors(
        IReadOnlyList<InventoryTankOverview> tanks,
        IReadOnlyDictionary<string, ArticleRecognitionColorsResult> colors)
    {
        return tanks.Select(tank =>
        {
            var article = tank.Rows.Count == 1 ? tank.Rows[0].Article : "";
            return !string.IsNullOrWhiteSpace(article) && colors.TryGetValue(article, out var resolved)
                ? tank with { RecognitionColors = resolved }
                : tank;
        }).ToList();
    }

    internal static InventoryTankOverview BuildTankOverview(
        MachineTankOption option,
        IReadOnlyList<InventoryPosition> stock)
    {
        var rows = stock
            .Where(x => string.Equals(x.Warehouse, option.Warehouse, StringComparison.OrdinalIgnoreCase))
            .Select(ToMachineStockRow)
            .ToList();

        if (rows.Count == 0)
        {
            return new InventoryTankOverview(
                option.Warehouse,
                option.WarehouseText,
                MachineStockStatuses.Empty,
                $"Auf {option.Warehouse} wurde kein Bestand ungleich 0 gefunden.",
                rows,
                null);
        }

        if (rows.Count > 1)
        {
            return new InventoryTankOverview(
                option.Warehouse,
                option.WarehouseText,
                MachineStockStatuses.Ambiguous,
                $"Auf {option.Warehouse} wurden mehrere Bestände ungleich 0 ({rows.Count}) gefunden. Artikel und Mix-Charge sind nicht eindeutig.",
                rows,
                null);
        }

        var current = rows[0];
        if (current.QuantityKg < 0m)
        {
            return new InventoryTankOverview(
                option.Warehouse,
                option.WarehouseText,
                MachineStockStatuses.InvalidStock,
                $"Auf {option.Warehouse} wurde ein negativer Bestand gefunden: {current.Article}, Charge {current.Batch}, {current.QuantityKg:0.###} {current.Unit}.",
                rows,
                null);
        }

        if (!string.Equals(current.Unit, "KGM", StringComparison.OrdinalIgnoreCase))
        {
            return new InventoryTankOverview(
                option.Warehouse,
                option.WarehouseText,
                MachineStockStatuses.InvalidStock,
                $"Der Maschinenbestand wird in der unerwarteten Mengeneinheit '{current.Unit}' geliefert.",
                rows,
                null);
        }

        return new InventoryTankOverview(
            option.Warehouse,
            option.WarehouseText,
            MachineStockStatuses.Unique,
            $"Eindeutiger Oxaion-Maschinenbestand: {current.Article} ({current.ArticleText}), Charge {current.Batch}, {current.QuantityKg:0.###} kg.",
            rows,
            null);
    }

    private async Task<IReadOnlyDictionary<string, ArticleRecognitionColorsResult>> ReadColorsAsync(
        IReadOnlyList<string> articles,
        CancellationToken ct)
    {
        var gate = new SemaphoreSlim(MaxColorConcurrency, MaxColorConcurrency);
        try
        {
            var tasks = articles.Select(async article =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    await using var colorSession = await _oxaion.ConnectAsync(ct);
                    var result = await ArticleRecognitionColorLookup.ReadAsync(colorSession, article, ct);
                    return new KeyValuePair<string, ArticleRecognitionColorsResult>(article, result);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return new KeyValuePair<string, ArticleRecognitionColorsResult>(
                        article,
                        new ArticleRecognitionColorsResult(
                            ArticleRecognitionColorStatuses.Unavailable,
                            article,
                            null,
                            null,
                            $"Erkennungsfarben konnten nicht gelesen werden: {ex.Message}"));
                }
                finally
                {
                    gate.Release();
                }
            }).ToArray();

            var resolved = await Task.WhenAll(tasks);
            return resolved.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            gate.Dispose();
        }
    }

    private static MachineStockRow ToMachineStockRow(InventoryPosition position) => new(
        position.Warehouse,
        position.Article,
        position.ArticleText,
        position.Batch,
        "",
        position.QuantityKg,
        position.Unit,
        "");
}
