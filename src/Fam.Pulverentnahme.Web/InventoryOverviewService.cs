namespace Fam.Pulverentnahme.Web;

/// <summary>
/// Read-only overview for the operator inventory page. ERP stock remains sourced from the
/// dedicated read-only inventory SQL and the confirmed machine-tank Oxaion list path.
/// Recognition colours are presentation aids only and never booking truth.
/// </summary>
public sealed class InventoryOverviewService
{
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
        var stock = await _inventory.ReadRpStockAsync(ct);
        var colors = new Dictionary<string, ArticleRecognitionColorsResult>(StringComparer.OrdinalIgnoreCase);
        var tanks = new List<InventoryTankOverview>();

        IReadOnlyList<MachineTankOption> options = [];
        try
        {
            options = await _machineTanks.ReadOptionsAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keep the powder-store list usable even when the tank lookup is temporarily unavailable.
            tanks.Add(new InventoryTankOverview(
                "", "", "UNAVAILABLE",
                $"Maschinentanks konnten nicht gelesen werden: {ex.Message}",
                [], null));
        }

        foreach (var option in options)
        {
            try
            {
                var result = await _machineTanks.ReadStockAsync(option.Warehouse, ct);
                if (result.RecognitionColors is not null && !string.IsNullOrWhiteSpace(result.Article))
                    colors[result.Article] = result.RecognitionColors;

                tanks.Add(new InventoryTankOverview(
                    option.Warehouse,
                    option.WarehouseText,
                    result.Status,
                    result.Message,
                    result.Rows,
                    result.RecognitionColors));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                tanks.Add(new InventoryTankOverview(
                    option.Warehouse,
                    option.WarehouseText,
                    "UNAVAILABLE",
                    $"Tankbestand konnte nicht gelesen werden: {ex.Message}",
                    [], null));
            }
        }

        var allArticles = stock.Select(x => x.Article)
            .Concat(tanks.SelectMany(x => x.Rows).Select(x => x.Article))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var article in allArticles)
        {
            if (colors.TryGetValue(article, out var existing)
                && existing.Status == ArticleRecognitionColorStatuses.Complete)
                continue;

            try
            {
                // Use a fresh Oxaion session per article. The confirmed US17000/US21000
                // characteristic path has its own screen context and must not inherit a previous
                // machine-stock or another article-characteristic list context.
                await using var colorSession = await _oxaion.ConnectAsync(ct);
                colors[article] = await ArticleRecognitionColorLookup.ReadAsync(colorSession, article, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                colors[article] = new ArticleRecognitionColorsResult(
                    ArticleRecognitionColorStatuses.Unavailable,
                    article,
                    null,
                    null,
                    $"Erkennungsfarben konnten nicht gelesen werden: {ex.Message}");
            }
        }

        tanks = tanks.Select(tank =>
        {
            var article = tank.Rows.Count == 1 ? tank.Rows[0].Article : "";
            return !string.IsNullOrWhiteSpace(article) && colors.TryGetValue(article, out var resolved)
                ? tank with { RecognitionColors = resolved }
                : tank;
        }).ToList();

        return new InventoryOverviewResult(stock, tanks, colors);
    }
}
