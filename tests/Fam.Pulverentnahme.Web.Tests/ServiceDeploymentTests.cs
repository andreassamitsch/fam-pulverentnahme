using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class ServiceDeploymentTests
{
    [Fact]
    public void RuntimeConfigurationUsesOneSqlLoginAndExplicitEnvironmentDatabases()
    {
        var source = ReadRepoFile("src", "Fam.Pulverentnahme.Web", "RuntimeConfiguration.cs");

        Assert.Contains("syncos_stg_102", source);
        Assert.Contains("syncos_prd_102", source);
        Assert.Contains("SqlServer", source);
        Assert.Contains("SqlUser", source);
        Assert.Contains("SqlPasswordProtected", source);
        Assert.Contains("OxaionStagingDatabase", source);
        Assert.Contains("OxaionProductionDatabase", source);
        Assert.Contains("DataProtectionScope.LocalMachine", source);
        Assert.Contains("ConfirmProduction", source);
    }

    [Fact]
    public void ServiceHostSeparatesIisBackendAndLocalAdminPort()
    {
        var program = ReadRepoFile("src", "Fam.Pulverentnahme.Web", "Program.cs");
        var settings = ReadRepoFile("src", "Fam.Pulverentnahme.Web", "appsettings.json");

        Assert.Contains("options.ListenLocalhost(serviceHost.MainPort)", program);
        Assert.Contains("options.ListenLocalhost(serviceHost.AdminPort)", program);
        Assert.Contains("StartsWithSegments(\"/api/admin\")", program);
        Assert.Contains("\"MainPort\": 5080", settings);
        Assert.Contains("\"AdminPort\": 5081", settings);
    }

    [Fact]
    public void MsiInstallsAutoServiceAndStopsItDuringUpgrade()
    {
        var wix = ReadRepoFile("installer", "Package.wxs");

        Assert.Contains("Name=\"FAMPulverentnahme\"", wix);
        Assert.Contains("Start=\"auto\"", wix);
        Assert.Contains("Start=\"install\"", wix);
        Assert.Contains("Stop=\"both\"", wix);
        Assert.Contains("Remove=\"uninstall\"", wix);
        Assert.Contains("Wait=\"yes\"", wix);
    }

    [Fact]
    public void RuntimePersonnelSqlDoesNotHardcodeStagingDatabase()
    {
        var rfid = ReadRepoFile("src", "Fam.Pulverentnahme.Web", "RfidPersonnelService.cs");
        var auth = ReadRepoFile("src", "Fam.Pulverentnahme.Web", "PersonnelAuthentication.cs");

        Assert.DoesNotContain("syncos_stg_102.", rfid, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("syncos_stg_102.", auth, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[{schema}].[ITSUSER]", rfid);
        Assert.Contains("[{schema}].[ITSUSER]", auth);
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
