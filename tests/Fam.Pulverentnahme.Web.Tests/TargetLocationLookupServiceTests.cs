using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class TargetLocationLookupServiceTests
{
    [Fact]
    public void WarehouseLookupIsReadOnlyCompleteCaseInsensitiveAndUsesConfirmedWarehouseMaster()
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
        Assert.DoesNotContain("TOP (", sql, StringComparison.OrdinalIgnoreCase);
        AssertReadOnly(sql);
    }

    [Fact]
    public void StorageBinLookupUsesPclBinMasterBehindConfirmedLb13210Matchcode()
    {
        var sql = TargetLocationLookupService.StorageBinQueryText;
        Assert.Contains("FROM OXAION.LPCLAP AS P", sql);
        Assert.Contains("P.PCFIRM = @firm", sql);
        Assert.Contains("UPPER(P.PCLAGO) = UPPER(@warehouse)", sql);
        Assert.Contains("P.PCLAPL", sql);
        Assert.Contains("UPPER(P.PCLAPL) LIKE UPPER(@prefix)", sql);
        Assert.DoesNotContain("OXAION.LLPLAP", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TOP (", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RP.%", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LPLABE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LLPWEP", sql, StringComparison.OrdinalIgnoreCase);
        AssertReadOnly(sql);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("FAM", "FAM%")]
    [InlineData("fam", "fam%")]
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
