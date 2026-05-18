using System.Text.RegularExpressions;
using Xunit;

namespace RunFence.Tests;

public sealed class InstallerStaticTests
{
    [Fact]
    public void RunFenceWxs_UsesMachineRegistryKeyPathsForShortcutComponents()
    {
        var source = ReadRepoFile("installer", "RunFence.wxs");

        Assert.Matches(
            new Regex(@"<Component Id=""StartMenuShortcuts""[\s\S]*?<RegistryValue Root=""HKLM"" Key=""Software\\RunFence\\Install""\s+Type=""integer"" Value=""1"" KeyPath=""yes"" />",
                RegexOptions.Singleline),
            source);
        Assert.Matches(
            new Regex(@"<Component Id=""DesktopShortcut""[\s\S]*?<RegistryValue Root=""HKLM"" Key=""Software\\RunFence\\Install"" Name=""DesktopShortcut""\s+Type=""integer"" Value=""1"" KeyPath=""yes"" />",
                RegexOptions.Singleline),
            source);
        Assert.DoesNotContain(@"Root=""HKCU"" Key=""Software\RunFence\Install""", source, StringComparison.Ordinal);
        Assert.DoesNotContain(@"Root=""HKCU"" Key=""Software\RunFence\DesktopShortcut""", source, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(
            [AppContext.BaseDirectory, "..", "..", "..", "..", "..", .. relativePath]));
        return File.ReadAllText(fullPath);
    }
}
