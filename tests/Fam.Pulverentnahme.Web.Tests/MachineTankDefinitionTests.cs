using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class MachineTankDefinitionTests
{
    [Fact]
    public void MachineTanksComeFromConfirmedOxaionWarehouseType02()
    {
        var sql = MachineTankService.MachineTankWarehouseSql;

        Assert.Contains("FROM OXAION.ULGSTP", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LG.LGFIRM = @firm", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LG.LGLGART = N'02'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LG.LGLAGO", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LG.LGBEZC", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppSettingsDoesNotContainStaticMachineTankWhitelist()
    {
        var json = ReadRepoFile("src", "Fam.Pulverentnahme.Web", "appsettings.json");

        Assert.DoesNotContain("\"MachineTanks\"", json);
        Assert.DoesNotContain("\"Warehouses\": [ \"EOS1\", \"EOS2\" ]", json);
    }

    [Fact]
    public void FrontendRefreshesDynamicTankListBeforeValidatingScan()
    {
        var app = ReadRepoFile("src", "Fam.Pulverentnahme.Web", "wwwroot", "app.js");
        var process = ReadRepoFile("src", "Fam.Pulverentnahme.Web", "wwwroot", "process-mode.js");

        Assert.Contains("await loadMachines();const option=machines.find", app);
        Assert.Contains("await loadMachines();const m=machines.find", process);
        Assert.Contains("Lagerortart 02", app);
        Assert.Contains("Lagerortart 02", process);
    }

    private static string ReadRepoFile(params string[] relativeParts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
                return File.ReadAllText(Path.Combine([directory.FullName, .. relativeParts]));

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root containing AGENTS.md was not found.");
    }
}
