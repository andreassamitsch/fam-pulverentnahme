using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class InventoryServiceTests
{
    [Fact]
    public void InventorySqlIsReadOnlyAndCoversBinManagedAndNonBinManagedWarehouses()
    {
        var sql = InventoryService.QueryText;

        Assert.Contains("FROM OXAION.LLPLAP AS LP", sql);
        Assert.Contains("INNER JOIN OXAION.LLPWEP AS B", sql);
        Assert.Contains("L.LGKLPL = N'J'", sql);
        Assert.Contains("FROM OXAION.LLAWEP AS LA", sql);
        Assert.Contains("L.LGKLPL = N'N'", sql);
        Assert.Contains("LA.LAGRKZ <> N'J'", sql);
        Assert.Contains("LP.LPIDNR LIKE N'RP.%'", sql);
        Assert.Contains("LA.LAIDNR LIKE N'RP.%'", sql);
        Assert.Contains("B.LPLABE <> 0", sql);
        Assert.Contains("LA.LALABE <> 0", sql);
        Assert.Contains("LP.LPFIRM = @firm", sql);
        Assert.Contains("LA.LAFIRM = @firm", sql);

        Assert.DoesNotContain("INSERT ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MERGE ", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InventorySqlReturnsBatchMetadataUsedByWorkerView()
    {
        var sql = InventoryService.QueryText;

        Assert.Contains("Artikelbezeichnung", sql);
        Assert.Contains("ChargeDatum", sql);
        Assert.Contains("Einheit", sql);
        Assert.Contains("Lagerplatz", sql);
        Assert.Contains("TRIM(X.Charge) AS Charge", sql);
    }
}
