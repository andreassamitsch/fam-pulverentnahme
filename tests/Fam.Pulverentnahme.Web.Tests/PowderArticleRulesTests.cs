using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class PowderArticleRulesTests
{
    [Theory]
    [InlineData("RP.00010", true)]
    [InlineData("PB.00001", true)]
    [InlineData("pb.00001", true)]
    [InlineData(" RP.00024 ", true)]
    [InlineData("RP", false)]
    [InlineData("PB", false)]
    [InlineData("PA.00001", false)]
    [InlineData("VK.00001", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void RecognizesOnlyPowderArticleGroups(string? article, bool expected)
    {
        Assert.Equal(expected, PowderArticleRules.IsPowderArticle(article));
    }

    [Fact]
    public void CustomerSupplyFlagDistinguishesRpAndPb()
    {
        Assert.True(PowderArticleRules.IsCustomerSupplied("PB.00001"));
        Assert.True(PowderArticleRules.IsCustomerSupplied(" pb.00001 "));
        Assert.False(PowderArticleRules.IsCustomerSupplied("RP.00010"));
    }

    [Fact]
    public void FrontendRecognizesBothArticleGroupsAndInvalidatesOldShell()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "src", "Fam.Pulverentnahme.Web", "wwwroot");
        var shell = File.ReadAllText(Path.Combine(web, "process-shell.js"));
        var ux = File.ReadAllText(Path.Combine(web, "process-ux-optimizations.js"));
        var page = File.ReadAllText(Path.Combine(web, "process-mode.js"));
        var html = File.ReadAllText(Path.Combine(web, "index.html"));
        var worker = File.ReadAllText(Path.Combine(web, "sw.js"));

        Assert.Contains(@"(?:RP|PB)\.", shell);
        Assert.Contains(@"(?:RP|PB)\.", ux);
        Assert.Contains("Kundenbeistellung", page);
        Assert.Contains("RP.*- oder PB.*-Bestände", page);

        foreach (var file in new[] { "process-shell.js", "process-ux-optimizations.js", "process-mode.js" })
        {
            Assert.Contains("/" + file + "?v=" + (file == "process-mode.js" ? "20261008-origin-ui-fix-2" : "20261008-pb-powder-1"), html);
            Assert.Contains("/" + file + "?v=" + (file == "process-mode.js" ? "20261008-origin-ui-fix-2" : "20261008-pb-powder-1"), worker);
        }
        Assert.Contains("fam-pulver-v53-charge-origin-ui-fix-20261008", worker);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root containing AGENTS.md was not found.");
    }
}
