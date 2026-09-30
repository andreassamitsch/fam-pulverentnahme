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

        const string processModeUrl = "/process-mode.js?v=20260930-label-print-reprint-1";
        Assert.Contains(processModeUrl, index);
        Assert.Contains(processModeUrl, serviceWorker);
        Assert.Contains("fam-pulver-staging-v36-label-print-reprint-20260930", serviceWorker);
        Assert.Contains("Etiketten drucken?", processMode);
        Assert.Contains("data-mode=\"label-reprint\"", processMode);
        Assert.Contains("Etiketten nachdrucken", processMode);
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
