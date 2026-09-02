using System.Xml.Linq;
using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class SourceStockParsingTests
{
    [Fact]
    public void ParseArticleWarehouseRows_ReadsWarehouseBatchAndPositiveStock()
    {
        var xml = XDocument.Parse("""
            <PARM><TABLE>
              <ROW>
                <KEY><LALAGO>H04HRL</LALAGO><LAIDNR>RP.00010</LAIDNR><LAPONR>84671</LAPONR><LAFIRM>103</LAFIRM></KEY>
                <LLAWEL01PONR.LALABE>1024,713</LLAWEL01PONR.LALABE>
              </ROW>
              <STOP />
            </TABLE></PARM>
            """);

        var rows = SourceStockService.ParseArticleWarehouseRows(xml);

        var row = Assert.Single(rows);
        Assert.Equal("H04HRL", row.Warehouse);
        Assert.Equal("RP.00010", row.Article);
        Assert.Equal("84671", row.Batch);
        Assert.Equal(1024.713m, row.QuantityKg);
    }

    [Fact]
    public void ParsePositionRows_PreservesExactInternalStorageBinKey()
    {
        var xml = XDocument.Parse("""
            <PARM><TABLE>
              <ROW>
                <KEY>
                  <LPPONR>84671</LPPONR>
                  <LPLAPL>RE1F3</LPLAPL>
                  <LPFIRM>103</LPFIRM>
                  <LPLAGO>H04HRL</LPLAGO>
                  <LPLHMN>1</LPLHMN>
                  <LPIDNR>RP.00010</LPIDNR>
                </KEY>
                <LLPWEP.LPLAPL>RE1F3</LLPWEP.LPLAPL>
                <LLPWEP.LPPONR>84671</LLPWEP.LPPONR>
                <LLPWEP.LPLABE>1024,713 kg</LLPWEP.LPLABE>
              </ROW>
              <STOP />
            </TABLE></PARM>
            """);

        var rows = SourceStockService.ParsePositionRows(xml, "Halle 04 Hochregallager");

        var row = Assert.Single(rows);
        Assert.Equal("H04HRL", row.Warehouse);
        Assert.Equal("Halle 04 Hochregallager", row.WarehouseText);
        Assert.Equal("RE1F3", row.StorageBin);
        Assert.DoesNotContain(' ', row.StorageBin);
        Assert.Equal("84671", row.Batch);
        Assert.Equal(1024.713m, row.QuantityKg);
        Assert.Equal("KGM", row.Unit);
    }

    [Fact]
    public void ParsePositionRows_KeepsDifferentBinsSeparateForSameArticle()
    {
        var xml = XDocument.Parse("""
            <PARM><TABLE>
              <ROW>
                <KEY><LPPONR>88673</LPPONR><LPLAPL>RE1F2</LPLAPL><LPFIRM>103</LPFIRM><LPLAGO>H04HRL</LPLAGO><LPIDNR>RP.00010</LPIDNR></KEY>
                <LLPWEP.LPLABE>600,000 kg</LLPWEP.LPLABE>
              </ROW>
              <ROW>
                <KEY><LPPONR>84671</LPPONR><LPLAPL>RE1F3</LPLAPL><LPFIRM>103</LPFIRM><LPLAGO>H04HRL</LPLAGO><LPIDNR>RP.00010</LPIDNR></KEY>
                <LLPWEP.LPLABE>1024,713 kg</LLPWEP.LPLABE>
              </ROW>
              <STOP />
            </TABLE></PARM>
            """);

        var rows = SourceStockService.ParsePositionRows(xml, "Halle 04 Hochregallager");

        Assert.Collection(rows,
            first =>
            {
                Assert.Equal("RE1F2", first.StorageBin);
                Assert.Equal("88673", first.Batch);
                Assert.Equal(600.000m, first.QuantityKg);
            },
            second =>
            {
                Assert.Equal("RE1F3", second.StorageBin);
                Assert.Equal("84671", second.Batch);
                Assert.Equal(1024.713m, second.QuantityKg);
            });
    }
}
