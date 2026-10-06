using System.Text.RegularExpressions;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class FrontendBootstrapTests
{
    [Fact]
    public void ProcessModeRouterIsLoadedExactlyOnce()
    {
        var root = FindRepositoryRoot();
        var webRoot = Path.Combine(root, "src", "Fam.Pulverentnahme.Web", "wwwroot");
        var index = File.ReadAllText(Path.Combine(webRoot, "index.html"));
        var articleColors = File.ReadAllText(Path.Combine(webRoot, "article-colors.js"));

        Assert.Equal(1, Regex.Matches(index, "<script\\s+src=\"/process-mode\\.js(?:\\?[^\"]*)?\"", RegexOptions.IgnoreCase).Count);
        Assert.DoesNotContain("process-mode.js", articleColors);
    }

    [Fact]
    public void LabelPrintUiIsPresentAndUsesFreshServiceWorkerVersion()
    {
        var root = FindRepositoryRoot();
        var webRoot = Path.Combine(root, "src", "Fam.Pulverentnahme.Web", "wwwroot");
        var index = File.ReadAllText(Path.Combine(webRoot, "index.html"));
        var serviceWorker = File.ReadAllText(Path.Combine(webRoot, "sw.js"));
        var processMode = File.ReadAllText(Path.Combine(webRoot, "process-mode.js"));

        const string processModeUrl = "/process-mode.js?v=20261005-inventory-speed-colors-1";
        Assert.Contains(processModeUrl, index);
        Assert.Contains(processModeUrl, serviceWorker);
        Assert.Contains("fam-pulver-v46-empty-replenish-tank-20261006", serviceWorker);
        Assert.Contains("keys.filter(k=>k.startsWith('fam-pulver-')&&k!==CACHE)", serviceWorker);
        Assert.Contains("Etiketten drucken?", processMode);
        Assert.Contains("data-mode=\"label-reprint\"", processMode);
        Assert.Contains("Etiketten nachdrucken", processMode);
        Assert.Contains("/ui-config.js?v=20261005-user-timeout-1", index);
        Assert.Contains("/ui-config.js?v=20261005-user-timeout-1", serviceWorker);
        Assert.Contains("/personnel-auth.js?v=20261005-user-timeout-1", index);
        Assert.Contains("/personnel-auth.js?v=20261005-user-timeout-1", serviceWorker);
        Assert.Contains("/app.js?v=20261006-empty-replenish-tank-1", index);
        Assert.Contains("/app.js?v=20261006-empty-replenish-tank-1", serviceWorker);
        Assert.Contains("/submit.js?v=20261006-empty-replenish-tank-1", index);
        Assert.Contains("/submit.js?v=20261006-empty-replenish-tank-1", serviceWorker);
        Assert.Contains("/worker-enhancements.js?v=20261006-empty-replenish-tank-1", index);
        Assert.Contains("/worker-enhancements.js?v=20261006-empty-replenish-tank-1", serviceWorker);
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
