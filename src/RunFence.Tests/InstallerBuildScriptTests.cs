using System.Text.RegularExpressions;
using Xunit;

namespace RunFence.Tests;

public sealed class InstallerBuildScriptTests
{
    [Fact]
    public void RunFenceWxs_UsesRealArpLinks()
    {
        var source = ReadRepoFile("installer", "RunFence.wxs");

        Assert.Contains(
            @"<Property Id=""ARPHELPLINK"" Value=""https://github.com/runfence/RunFence/issues"" />",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            @"<Property Id=""ARPURLINFOABOUT"" Value=""https://github.com/runfence/RunFence"" />",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("your-repo-url-here", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildInstallerScript_ValidatesArpLinksBeforeInstallerBuild()
    {
        var source = ReadRepoFile("installer", "build-installer.ps1");

        Assert.Contains("function Assert-InstallerArpLinksConfigured", source, StringComparison.Ordinal);
        Assert.Contains("ARPHELPLINK", source, StringComparison.Ordinal);
        Assert.Contains("ARPURLINFOABOUT", source, StringComparison.Ordinal);
        Assert.Contains("your-repo-url-here|placeholder|example\\.com|contoso", source, StringComparison.Ordinal);
        Assert.Matches(
            new Regex(@"Assert-InstallerArpLinksConfigured ""\$PSScriptRoot/RunFence\.wxs""[\s\S]*# 0\. Ensure required WiX extensions are installed",
                RegexOptions.Singleline),
            source);
    }

    private static string ReadRepoFile(params string[] relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(
            [AppContext.BaseDirectory, "..", "..", "..", "..", "..", .. relativePath]));
        return File.ReadAllText(fullPath);
    }
}
