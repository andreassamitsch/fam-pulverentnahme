using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class InventoryOverviewOptimizationTests
{
    [Fact]
    public void TankOverviewUsesExistingReadOnlyInventoryRows()
    {
        var option = new MachineTankOption("M400-02", "EP-M400S-02-Tank");
        InventoryPosition[] stock =
        [
            new("RP.00024", "AlSi10Mg", "M400-02", "M400-02", "", "0001049", 120.100m, "KGM", false),
            new("RP.00024", "AlSi10Mg", "H04PULA", "H04PULA", "REGPL1F01", "0001049", 200m, "KGM", false)
        ];

        var result = InventoryOverviewService.BuildTankOverview(option, stock);

        Assert.Equal(MachineStockStatuses.Unique, result.Status);
        Assert.Single(result.Rows);
        Assert.Equal("M400-02", result.Rows[0].Warehouse);
        Assert.Equal("RP.00024", result.Rows[0].Article);
        Assert.Equal("0001049", result.Rows[0].Batch);
        Assert.Equal(120.100m, result.Rows[0].QuantityKg);
    }

    [Fact]
    public void TankOverviewReportsEmptyAndAmbiguousStatesFromInventoryRows()
    {
        var option = new MachineTankOption("EOS1", "EOS 1 -Tank");

        var empty = InventoryOverviewService.BuildTankOverview(option, []);
        Assert.Equal(MachineStockStatuses.Empty, empty.Status);

        InventoryPosition[] ambiguousStock =
        [
            new("RP.00010", "AlSi10Mg", "EOS1", "EOS1", "", "A", 10m, "KGM", false),
            new("RP.00010", "AlSi10Mg", "EOS1", "EOS1", "", "B", 20m, "KGM", false)
        ];

        var ambiguous = InventoryOverviewService.BuildTankOverview(option, ambiguousStock);
        Assert.Equal(MachineStockStatuses.Ambiguous, ambiguous.Status);
        Assert.Equal(2, ambiguous.Rows.Count);
    }

    [Fact]
    public void SameRecognitionColorsAreUsedForTankAndPowderArticle()
    {
        var colors = new Dictionary<string, ArticleRecognitionColorsResult>(StringComparer.OrdinalIgnoreCase)
        {
            ["RP.00024"] = new(
                ArticleRecognitionColorStatuses.Complete,
                "RP.00024",
                new ArticleRecognitionColor("EFA01", "FF0000", "Rot"),
                new ArticleRecognitionColor("EFA02", "8B4513", "Braun"),
                "Erkennungsfarben vollständig.")
        };

        var tank = InventoryOverviewService.BuildTankOverview(
            new MachineTankOption("M400-02", "EP-M400S-02-Tank"),
            [
                new InventoryPosition("RP.00024", "AlSi10Mg", "M400-02", "M400-02", "", "0001049", 120.100m, "KGM", false)
            ]);

        var mapped = InventoryOverviewService.ApplyRecognitionColors([tank], colors);

        Assert.Same(colors["RP.00024"], mapped[0].RecognitionColors);
        Assert.Equal("FF0000", mapped[0].RecognitionColors!.Color1!.Hex);
        Assert.Equal("8B4513", mapped[0].RecognitionColors!.Color2!.Hex);
    }

    [Fact]
    public void InventoryOverviewNoLongerPerformsSerialTankHttpReads()
    {
        var source = ReadRepoFile("src", "Fam.Pulverentnahme.Web", "InventoryOverviewService.cs");

        Assert.DoesNotContain("_machineTanks.ReadStockAsync", source);
        Assert.Contains("Task.WhenAll", source);
        Assert.Contains("MaxColorConcurrency = 3", source);
        Assert.Contains("tankWarehouses.Contains(x.Warehouse)", source);
    }

    private static string ReadRepoFile(params string[] relativeParts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
                return File.ReadAllText(Path.Combine([directory.FullName, .. relativeParts]));

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root containing AGENTS.md was not found.");
    }
}
