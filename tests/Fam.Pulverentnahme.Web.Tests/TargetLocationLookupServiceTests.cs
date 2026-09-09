using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class TargetLocationLookupServiceTests
{
    [Fact]
    public void WarehouseLookupIsReadOnlyCaseInsensitiveAndUsesConfirmedWarehouseMaster()
    {
        var sql = TargetLocationLookupService.WarehouseQueryText;
        Assert.Contains("FROM OXAION.ULGSTP AS L", sql);
        Assert.Contains("L.LGFIRM = @firm", sql);
        Assert.Contains("L.LGBFRM = @firm", sql);
        Assert.Contains("L.LGLOKZ = N''", sql);
        Assert.Contains("L.LGKLPL = N'J'", sql);
        Assert.Contains("@excludeWarehouse", sql);
        Assert.Contains("UPPER(L.LGLAGO) <> UPPER(@excludeWarehouse)", sql);
        Assert.Contains("UPPER(L.LGLAGO) LIKE UPPER(@prefix)", sql);
        AssertReadOnly(sql);
    }

    [Fact]
    public void StorageBinLookupIsReadOnlyCaseInsensitiveAndReturnsInternalBinKeysWithoutPowderOrStockFilter()
    {
        var sql = TargetLocationLookupService.StorageBinQueryText;
        Assert.Contains("FROM OXAION.LLPLAP AS LP", sql);
        Assert.Contains("LP.LPFIRM = @firm", sql);
        Assert.Contains("UPPER(LP.LPLAGO) = UPPER(@warehouse)", sql);
        Assert.Contains("LP.LPLAPL", sql);
        Assert.Contains("UPPER(LP.LPLAPL) LIKE UPPER(@prefix)", sql);
        Assert.DoesNotContain("RP.%", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LPLABE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LLPWEP", sql, StringComparison.OrdinalIgnoreCase);
        AssertReadOnly(sql);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("FAM", "FAM%")]
    [InlineData("A%_\\[", "A\\%\\_\\\\\\[%")]
    public void PrefixEscapesSqlLikeWildcards(string input, string expected) =>
        Assert.Equal(expected, TargetLocationLookupService.Prefix(input, 40));

    private static void AssertReadOnly(string sql)
    {
        Assert.DoesNotContain("INSERT ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MERGE ", sql, StringComparison.OrdinalIgnoreCase);
    }
}
