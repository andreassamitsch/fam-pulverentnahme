using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class FillNewServiceTests
{
    [Fact]
    public void FirstSourceUsesLfThenTankRebatchUsesLm()
    {
        var request = Request([
            new AdditionalPowderSource("FAMLAB", "FAM LABOR", "RE1F1", "SOURCE_A", 10m)
        ]);

        var specs = FillNewService.BuildTransferSpecs(request);

        Assert.Equal(2, specs.Count);
        var transfer = specs[0];
        Assert.Equal(1, transfer.Position);
        Assert.Equal("LF", transfer.BookingKey);
        Assert.Equal("FAMLAB", transfer.FromWarehouse);
        Assert.Equal("RE1F1", transfer.FromStorageBin);
        Assert.Equal("SOURCE_A", transfer.FromBatch);
        Assert.Equal("EOS1", transfer.ToWarehouse);
        Assert.Equal("", transfer.ToBatch);

        var rebatch = specs[1];
        Assert.Equal(2, rebatch.Position);
        Assert.Equal("LM", rebatch.BookingKey);
        Assert.Equal("EOS1", rebatch.FromWarehouse);
        Assert.Equal("SOURCE_A", rebatch.FromBatch);
        Assert.Equal("EOS1", rebatch.ToWarehouse);
        Assert.Equal("RP00010MIX_20260908_101010", rebatch.ToBatch);
        Assert.Equal(10m, rebatch.QuantityKg);
    }

    [Fact]
    public void AdditionalSourcesJoinSameGeneratedMixAfterInitialLfAndRebatch()
    {
        var request = Request([
            new AdditionalPowderSource("FAMLAB", "FAM LABOR", "RE1F1", "SOURCE_A", 10m),
            new AdditionalPowderSource("H04KDX", "Halle 04 Kardex", "LL312", "SOURCE_B", 2.5m)
        ]);

        var specs = FillNewService.BuildTransferSpecs(request);

        Assert.Equal(3, specs.Count);
        var additional = specs[2];
        Assert.Equal(3, additional.Position);
        Assert.Equal("LM", additional.BookingKey);
        Assert.Equal("H04KDX", additional.FromWarehouse);
        Assert.Equal("LL312", additional.FromStorageBin);
        Assert.Equal("SOURCE_B", additional.FromBatch);
        Assert.Equal("EOS1", additional.ToWarehouse);
        Assert.Equal("RP00010MIX_20260908_101010", additional.ToBatch);
        Assert.Equal(2.5m, additional.QuantityKg);
    }

    private static FillNewRequest Request(IReadOnlyList<AdditionalPowderSource> sources) => new(
        "test-operation",
        "446",
        "Test User",
        "EOS1",
        "EOS 1 -Tank",
        "RP.00010",
        "AlSi10Mg",
        "RP00010MIX_20260908_101010",
        new DateOnly(2026, 9, 8),
        new DateOnly(2026, 9, 8),
        sources);
}
