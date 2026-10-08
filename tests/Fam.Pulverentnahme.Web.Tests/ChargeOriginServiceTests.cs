using System.Xml.Linq;
using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class ChargeOriginServiceTests
{
    [Fact]
    public void BuildsCapturedChargeOriginUsageContext()
    {
        var fields = ChargeOriginService.BuildUsageContext(
            "RP.00010",
            "RP00010MIX_20261006_090853",
            0);

        Assert.Equal("CH", fields["TX_USAGE"]);
        Assert.Equal("0", fields["POOBID"]);
        Assert.Equal("0", fields["FIOBID"]);
        Assert.Equal("0", fields["FIPOBID"]);
        Assert.Equal("UPOST", fields["KEYTYPE"]);
        Assert.Equal("RP.00010", fields["POIDNR"]);
        Assert.Equal("RP.00010", fields["QHIDNR"]);
        Assert.Equal("RP.00010", fields["SEIDNR"]);
        Assert.Equal("RP00010MIX_20261006_090853", fields["POPONR"]);
        Assert.Equal("RP00010MIX_20261006_090853", fields["QHPONR"]);
        Assert.Equal("RP00010MIX_20261006_090853", fields["SEPONR"]);
    }

    [Fact]
    public void ParsesCapturedTreeRowAndNormalizesArticleDisplayValue()
    {
        var xml = XDocument.Parse("""
            <PARM><TABLE>
              <ROW SUBTREES="TRUE">
                <KEY>
                  <PEMPOS>00001.00026</PEMPOS>
                  <PESSID>ANSA9144406858836792</PESSID>
                </KEY>
                <UPOVEP.PESTCK>2</UPOVEP.PESTCK>
                <UPOVEP.PEPONR>RP00010MIX_20261001_161906</UPOVEP.PEPONR>
                <UPOVEP.PEIDNR>RP.00010              RP.00010</UPOVEP.PEIDNR>
                <UPOVEP.PELINR />
                <UPOVEP.PEBENR />
                <UPOVEP.PELFNR />
                <UPOVEP.PEFAUN />
                <UPOVEP.PEWEGN />
              </ROW>
              <STOP />
            </TABLE></PARM>
            """);

        var row = Assert.Single(ChargeOriginService.ParseRows(xml));

        Assert.True(row.HasSubtrees);
        Assert.Equal(2, row.Level);
        Assert.Equal("ANSA9144406858836792", row.Pessid);
        Assert.Equal("00001.00026", row.Pempos);
        Assert.Equal("RP.00010", row.Article);
        Assert.Equal("RP00010MIX_20261001_161906", row.Batch);
    }

    [Fact]
    public void SelectsUniqueBaseBatchesAndExcludesEveryBatchThatHasSubtrees()
    {
        var rows = new[]
        {
            new ChargeOriginRow(
                "S", "00001", true, 1,
                "RP.00010", "RP00010MIX_20261006_090853",
                "", "", "", "", ""),

            // The captured Oxaion tree contains the same ground batch several times:
            // once with procurement origin and repeatedly through later FA usage.
            new ChargeOriginRow(
                "S", "00001.00001", false, 2,
                "RP.00010", "84671",
                "3001399 000", "FA24BE00022", "", "", "FA24WE00027"),
            new ChargeOriginRow(
                "S", "00001.00002", false, 2,
                "RP.00010", "84671",
                "", "", "", "FA24FI00088", ""),

            // One visible usage row plus one expandable row for the same intermediate MIX.
            new ChargeOriginRow(
                "S", "00001.00025", false, 2,
                "RP.00010", "RP00010MIX_20261001_161906",
                "", "", "", "FA24FI00119", ""),
            new ChargeOriginRow(
                "S", "00001.00026", true, 2,
                "RP.00010", "RP00010MIX_20261001_161906",
                "", "", "", "", ""),

            // A second terminal batch discovered deeper in the tree.
            new ChargeOriginRow(
                "S", "00001.00026.00001", false, 3,
                "RP.00010", "87911",
                "3002000 000", "FA25BE00077", "", "", "FA25WE00040")
        };

        var result = ChargeOriginService.SelectBaseBatches(rows);

        Assert.Equal(2, result.Count);

        var first = Assert.Single(result.Where(x => x.Batch == "84671"));
        Assert.Equal("RP.00010", first.Article);
        Assert.Equal("3001399 000", first.Supplier);
        Assert.Equal("FA24BE00022", first.PurchaseOrder);
        Assert.Equal("FA24WE00027", first.GoodsReceipt);
        Assert.Equal(2, first.FirstLevel);

        var second = Assert.Single(result.Where(x => x.Batch == "87911"));
        Assert.Equal("3002000 000", second.Supplier);

        Assert.DoesNotContain(result, x => x.Batch.StartsWith("RP00010MIX_", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsTreePageWithoutStopMarkerInsteadOfReturningPartialOrigin()
    {
        var xml = XDocument.Parse("""
            <PARM><TABLE>
              <ROW>
                <KEY><PEMPOS>00001</PEMPOS><PESSID>S</PESSID></KEY>
                <UPOVEP.PEPONR>84671</UPOVEP.PEPONR>
                <UPOVEP.PEIDNR>RP.00010</UPOVEP.PEIDNR>
              </ROW>
            </TABLE></PARM>
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => ChargeOriginService.EnsureCompletePage(xml));

        Assert.Contains("STOP", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("RP.00010              RP.00010", "RP.00010")]
    [InlineData("RP.00010", "RP.00010")]
    [InlineData("RP.00012 316L", "RP.00012")]
    public void NormalizesOxaionArticleDisplay(string raw, string expected)
    {
        Assert.Equal(expected, ChargeOriginService.NormalizeArticleField(raw));
    }
}
