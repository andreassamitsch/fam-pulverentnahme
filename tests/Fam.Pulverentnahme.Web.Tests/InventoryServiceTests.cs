using System.Xml.Linq;
using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class InventoryServiceTests
{
    [Fact]
    public void ParsesMultipleRpArticlesFromOneWarehouseBinList()
    {
        var xml = XDocument.Parse("""
            <PARM><TABLE>
              <ROW>
                <KEY><LPLAGO>H04HRL</LPLAGO><LPIDNR>RP.00010</LPIDNR><LPLAPL>RE1F3</LPLAPL><LPPONR>84671</LPPONR></KEY>
                <IDNR.TLBEZG>AlSi10Mg</IDNR.TLBEZG>
                <LLPWEP.LPLABE>1024,713 KGM</LLPWEP.LPLABE>
              </ROW>
              <ROW>
                <KEY><LPLAGO>H04HRL</LPLAGO><LPIDNR>RP.00012</LPIDNR><LPLAPL>RE2F1</LPLAPL><LPPONR>MIX12</LPPONR></KEY>
                <IDNR.TLBEZG>316L</IDNR.TLBEZG>
                <LLPWEP.LPLABE>-0,250 KGM</LLPWEP.LPLABE>
              </ROW>
              <ROW>
                <KEY><LPLAGO>H04HRL</LPLAGO><LPIDNR>VK.00001</LPIDNR><LPLAPL>RE9F9</LPLAPL><LPPONR>X</LPPONR></KEY>
                <LLPWEP.LPLABE>5,000 KGM</LLPWEP.LPLABE>
              </ROW>
              <STOP />
            </TABLE></PARM>
            """);

        var rows = InventoryService.ParseInventoryBinRows(xml, "Halle 04 Hochregallager");

        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, r => r.Article == "RP.00010" && r.StorageBin == "RE1F3" && r.Batch == "84671" && r.QuantityKg == 1024.713m);
        Assert.Contains(rows, r => r.Article == "RP.00012" && r.StorageBin == "RE2F1" && r.QuantityKg == -0.250m);
        Assert.Contains(rows, r => r.Article == "VK.00001" && r.QuantityKg == 5m);
    }

    [Fact]
    public void UsesInternalStorageBinKeyFromOxaionKey()
    {
        var xml = XDocument.Parse("""
            <PARM><TABLE><ROW>
              <KEY><LPLAGO>H04HRL</LPLAGO><LPIDNR>RP.00010</LPIDNR><LPLAPL>RE1F3</LPLAPL><LPPONR>84671</LPPONR></KEY>
              <LLPWEP.LPLAPL>RE1  F 3</LLPWEP.LPLAPL>
              <LLPWEP.LPLABE>1,000 KGM</LLPWEP.LPLABE>
            </ROW><STOP /></TABLE></PARM>
            """);

        var row = Assert.Single(InventoryService.ParseInventoryBinRows(xml, "H04HRL"));
        Assert.Equal("RE1F3", row.StorageBin);
    }
}
