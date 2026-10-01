using System.Data;
using Fam.Pulverentnahme.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class InventoryServiceTests
{
    [Fact]
    public void InventorySqlIsReadOnlyAndCoversBinManagedAndNonBinManagedWarehouses()
    {
        var sql = InventoryService.QueryText;

        Assert.Contains("FROM OXAION.LLPWEP AS B", sql);
        Assert.DoesNotContain("FROM OXAION.LLPLAP AS LP", sql);
        Assert.Contains("FROM OXAION.LLAWEP AS LA", sql);
        Assert.DoesNotContain("INNER JOIN OXAION.ULGSTP", sql);
        Assert.Contains("LA.LAGRKZ <> N'J'", sql);
        Assert.Contains("NOT EXISTS", sql);
        Assert.Contains("FROM OXAION.LLPWEP AS BX", sql);
        Assert.Contains("BX.LPPONR = LA.LAPONR", sql);
        Assert.Contains("SELECT DISTINCT", sql);
        Assert.Contains("B.LPIDNR LIKE N'RP.%'", sql);
        Assert.Contains("LA.LAIDNR LIKE N'RP.%'", sql);
        Assert.Contains("B.LPLABE <> 0", sql);
        Assert.Contains("LA.LALABE <> 0", sql);
        Assert.Contains("B.LPFIRM = @firm", sql);
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

    [Fact]
    public void InventoryReaderAllowsNamedColumnsInMapperOrder()
    {
        Assert.Equal(CommandBehavior.Default, InventoryService.ReaderBehavior);
    }

    [Fact]
    public void InventoryServiceDoesNotReuseSyncosConnectionOptions()
    {
        var ctor = Assert.Single(typeof(InventoryService).GetConstructors());
        var parameters = ctor.GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.Contains(typeof(IConfiguration), parameters);
        Assert.Contains(typeof(IOptions<OxaionOptions>), parameters);
        Assert.DoesNotContain(typeof(IOptions<SyncosOptions>), parameters);
    }
}
