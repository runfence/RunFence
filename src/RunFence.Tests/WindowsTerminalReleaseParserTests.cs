using RunFence.Account.UI;
using Xunit;

namespace RunFence.Tests;

public sealed class WindowsTerminalReleaseParserTests
{
    [Theory]
    [InlineData("x64", "1.2.3.4")]
    [InlineData("x86", "1.2.3.4")]
    [InlineData("arm64", "1.2.3.4")]
    public void ParseLatestRelease_WhenMatchingAssetExists_ReturnsMatchingArchitecture(string architecture, string versionText)
    {
        var parser = new WindowsTerminalReleaseParser();
        var nonMatchingAssets = string.Join(
            ",\n                ",
            new[] { "x64", "x86", "arm64" }
                .Where(candidate => !string.Equals(candidate, architecture, StringComparison.Ordinal))
                .Select(candidate =>
                    $$"""{ "name": "Microsoft.WindowsTerminal_1.2.3.4_{{candidate}}.zip", "browser_download_url": "https://example.invalid/{{candidate}}-other.zip" }"""));
        var json = $$"""
            {
              "assets": [
                { "name": null, "browser_download_url": "https://example.invalid/skip-null-name.zip" },
                { "name": "  ", "browser_download_url": "https://example.invalid/skip-whitespace-name.zip" },
                { "name": "Microsoft.WindowsTerminal_1.2.3.4_x64.zip", "browser_download_url": null },
                {{nonMatchingAssets}},
                { "name": "Microsoft.WindowsTerminal_9.9.9.9_x64.msix", "browser_download_url": "https://example.invalid/not-a-zip.msix" },
                { "name": "Microsoft.WindowsTerminal_{{versionText}}_{{architecture}}.zip", "browser_download_url": "https://example.invalid/{{architecture}}.zip" }
              ]
            }
            """;

        var release = parser.ParseLatestRelease(json, architecture);

        Assert.Equal(Version.Parse(versionText), release.Version);
        Assert.Equal($"Microsoft.WindowsTerminal_{versionText}_{architecture}.zip", release.AssetName);
        Assert.Equal($"https://example.invalid/{architecture}.zip", release.DownloadUrl);
    }

    [Fact]
    public void ParseLatestRelease_WhenBrowserDownloadUrlIsWhitespace_SkipsAsset()
    {
        var parser = new WindowsTerminalReleaseParser();
        var json = """
            {
              "assets": [
                { "name": "Microsoft.WindowsTerminal_1.2.3.4_x64.zip", "browser_download_url": "   " },
                { "name": "Microsoft.WindowsTerminal_1.2.3.5_x64.zip", "browser_download_url": "https://example.invalid/x64.zip" }
              ]
            }
            """;

        var release = parser.ParseLatestRelease(json, "x64");

        Assert.Equal(new Version(1, 2, 3, 5), release.Version);
    }

    [Fact]
    public void ParseLatestRelease_WhenMatchingZipAssetIsMissing_ThrowsCurrentMessage()
    {
        var parser = new WindowsTerminalReleaseParser();
        var json = """
            {
              "assets": [
                { "name": "Microsoft.WindowsTerminal_1.2.3.4_x86.zip", "browser_download_url": "https://example.invalid/x86.zip" },
                { "name": "unrelated.txt", "browser_download_url": "https://example.invalid/unrelated.txt" }
              ]
            }
            """;

        var exception = Assert.Throws<InvalidOperationException>(() => parser.ParseLatestRelease(json, "x64"));

        Assert.Equal("Windows Terminal latest release did not contain a x64 ZIP asset.", exception.Message);
    }
}
