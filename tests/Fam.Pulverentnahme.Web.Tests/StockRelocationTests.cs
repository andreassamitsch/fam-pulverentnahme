namespace Fam.Pulverentnahme.Web.Tests;

public sealed class StockRelocationTests
{
    [Fact]
    public void BuildsSingleConfirmedLfLeTransferWithSourceAndTargetBins()
    {
        var request = Request();

        var specs = StockRelocationService.BuildTransferSpecs(request);

        var spec = Assert.Single(specs);
        Assert.Equal(1, spec.Position);
        Assert.Equal("LF", spec.BookingKey);
        Assert.Equal("RP.00024", spec.Article);
        Assert.Equal("H04PULA", spec.FromWarehouse);
        Assert.Equal("REGPL1F01", spec.FromStorageBin);
        Assert.Equal("0001049", spec.FromBatch);
        Assert.Equal("H04PULA", spec.ToWarehouse);
        Assert.Equal("REGPL2F01", spec.ToStorageBin);
        Assert.Equal("", spec.ToBatch);
        Assert.Equal(25.125m, spec.QuantityKg);

        var movements = new[]
        {
            new MovementRow("1", "LF", "RP.00024", "0001049", "H04PULA", "REGPL1F01", 25.125m, "1"),
            new MovementRow("1", "LE", "RP.00024", "0001049", "H04PULA", "REGPL2F01", 25.125m, "2")
        };

        Assert.True(MaterialTransferBookingService.MovementsComplete(specs, movements, out var message), message);
    }

    [Fact]
    public void RejectsIdenticalSourceAndTargetPosition()
    {
        var request = Request() with { TargetStorageBin = "REGPL1F01" };

        var ex = Assert.Throws<ArgumentException>(() => StockRelocationService.Validate(request));

        Assert.Contains("unterschiedliche Lagerplätze", ex.Message);
    }

    [Fact]
    public void RejectsQuantityAboveDisplayedSourceStock()
    {
        var request = Request() with { QuantityKg = 50.001m, ExpectedSourceQuantityKg = 50m };

        var ex = Assert.Throws<ArgumentException>(() => StockRelocationService.Validate(request));

        Assert.Contains("Quellbestand", ex.Message);
    }

    [Fact]
    public void InventoryUiPrefillsFullPositionQuantityAndBooksDedicatedOperation()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "Fam.Pulverentnahme.Web", "wwwroot", "process-mode.js"));

        Assert.Contains("inventoryRelocateBtn", source);
        Assert.Contains("amount:qty(row.quantityKg)", source);
        Assert.Contains("/api/stock-relocation", source);
        Assert.Contains("Charge bleibt unverändert", source);
        Assert.Contains("Tanklager werden nicht angeboten und serverseitig zusätzlich gesperrt", source);
    }

    [Fact]
    public void BackendFiltersAndRejectsTankWarehousesForRelocation()
    {
        var root = FindRepositoryRoot();
        var endpoints = File.ReadAllText(Path.Combine(root, "src", "Fam.Pulverentnahme.Web", "SeparateProcessEndpoints.cs"));
        var service = File.ReadAllText(Path.Combine(root, "src", "Fam.Pulverentnahme.Web", "StockRelocationService.cs"));

        Assert.Contains("/api/stock-relocation/target-warehouses", endpoints);
        Assert.Contains("!tankWarehouses.Contains(x.Warehouse)", endpoints);
        Assert.Contains("Umlagerung in ein Tanklager ist nicht zulässig.", endpoints);
        Assert.Contains("Umlagerung in ein Tanklager ist nicht zulässig.", service);
    }

    private static StockRelocationRequest Request() => new(
        "reloc-1",
        "0000000123",
        "Test Bediener",
        "RP.00024",
        "AlSi10Mg",
        "H04PULA",
        "H04PULA",
        "REGPL1F01",
        "0001049",
        50m,
        25.125m,
        "H04PULA",
        "REGPL2F01");

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root containing AGENTS.md was not found.");
    }
}
