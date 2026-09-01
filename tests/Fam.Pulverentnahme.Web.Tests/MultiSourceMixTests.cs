using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class MultiSourceMixTests
{
    [Fact]
    public void UsesLegacySingleSourceWhenAdditionalSourcesAreMissing()
    {
        var request = Request(additionalSources: null);
        var source = Assert.Single(MixRequestLogic.Sources(request));
        Assert.Equal("FAMLAB", source.Warehouse);
        Assert.Equal("87911", source.Batch);
        Assert.Equal(0.005m, source.AmountKg);
    }

    [Fact]
    public void RetryComparisonUsesStructuralMultiSourceEquality()
    {
        var sourcesA = new[]
        {
            new AdditionalPowderSource("FAMLAB", "FAM LABOR", "KA1", "87911", 0.005m),
            new AdditionalPowderSource("FAMLAB", "FAM LABOR", "KA1", "87912", 0.007m)
        };
        var sourcesB = sourcesA.Select(x => x with { }).ToArray();
        var a = Request("op-a", sourcesA);
        var b = Request("op-b", sourcesB) with { RetryOfClientOperationId = "op-a", SimulateFailure = null };

        Assert.True(MixRequestLogic.SameBookingData(a, b));
    }

    [Fact]
    public void RejectsInconsistentLegacyFirstSourceMirror()
    {
        var request = Request(additionalSources:
        [
            new AdditionalPowderSource("FAMLAB", "FAM LABOR", "KA1", "DIFFERENT", 0.005m),
            new AdditionalPowderSource("FAMLAB", "FAM LABOR", "KA1", "87912", 0.007m)
        ]);

        Assert.Throws<ArgumentException>(() => MixBookingService.ValidateRequest(request));
    }

    [Fact]
    public void RecognizesCompleteThreePositionDocument()
    {
        var request = Request(additionalSources:
        [
            new AdditionalPowderSource("FAMLAB", "FAM LABOR", "KA1", "87911", 0.005m),
            new AdditionalPowderSource("FAMLAB", "FAM LABOR", "KA1", "87912", 0.007m)
        ]);
        var rows = CompleteRows();

        var result = MixBookingService.AnalyzeMovements(request, rows);

        Assert.Equal("COMPLETE", result.Status);
        Assert.Equal(3, result.CompletedPositions);
        Assert.Equal(6, result.Rows.Count);
    }

    [Fact]
    public void RecoveryContinuesAfterLongestCompletePrefix()
    {
        var request = Request(additionalSources:
        [
            new AdditionalPowderSource("FAMLAB", "FAM LABOR", "KA1", "87911", 0.005m),
            new AdditionalPowderSource("FAMLAB", "FAM LABOR", "KA1", "87912", 0.007m)
        ]);
        var rows = CompleteRows().Take(4).ToArray();

        var result = MixBookingService.AnalyzeMovements(request, rows);

        Assert.Equal("PREFIX_COMPLETE", result.Status);
        Assert.Equal(2, result.CompletedPositions);
        Assert.Equal("CONTINUE_POSITION_3", result.Action);
    }

    private static RealMixRequest Request(
        string clientOperationId = "op-a",
        IReadOnlyList<AdditionalPowderSource>? additionalSources = null) => new(
        ClientOperationId: clientOperationId,
        PersonnelNo: "446",
        PersonnelName: "ANSA",
        Article: "RP.00010",
        ArticleText: "AlSi10Mg",
        OldMixWarehouse: "EOS1",
        OldMixWarehouseText: "EOS 1 -Tank",
        OldMixStorageBin: "",
        OldMixBatch: "RP10MIX",
        OldMixAmountKg: 164.330m,
        AddWarehouse: "FAMLAB",
        AddWarehouseText: "FAM LABOR",
        AddStorageBin: "KA1",
        AddBatch: "87911",
        AddAmountKg: 0.005m,
        TargetWarehouse: "EOS1",
        TargetWarehouseText: "EOS 1 -Tank",
        TargetStorageBin: "",
        TargetBatch: "RP10NEW",
        ProductionDate: new DateOnly(2026, 9, 1),
        BookingDate: new DateOnly(2026, 9, 1),
        BookingText: "Pulver nachfüllen EOS1",
        AdditionalSources: additionalSources);

    private static MovementRow[] CompleteRows() =>
    [
        new("1", "LM", "RP.00010", "RP10MIX", "EOS1", "", 164.330m, "t1"),
        new("1", "LN", "RP.00010", "RP10NEW", "EOS1", "", 164.330m, "t1"),
        new("2", "LM", "RP.00010", "87911", "FAMLAB", "KA1", 0.005m, "t2"),
        new("2", "LN", "RP.00010", "RP10NEW", "EOS1", "", 0.005m, "t2"),
        new("3", "LM", "RP.00010", "87912", "FAMLAB", "KA1", 0.007m, "t3"),
        new("3", "LN", "RP.00010", "RP10NEW", "EOS1", "", 0.007m, "t3")
    ];
}
