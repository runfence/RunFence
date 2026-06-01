using System.Text.Json;

namespace RunFence.Account.UI;

public sealed class WindowsTerminalReleaseParser
{
    public WindowsTerminalReleaseInfo ParseLatestRelease(string json, string architecture)
    {
        using var document = JsonDocument.Parse(json);
        foreach (var asset in document.RootElement.GetProperty("assets").EnumerateArray())
        {
            var assetName = TryGetString(asset, "name");
            var downloadUrl = TryGetString(asset, "browser_download_url");
            if (string.IsNullOrWhiteSpace(assetName) || string.IsNullOrWhiteSpace(downloadUrl))
                continue;

            if (!WindowsTerminalDeploymentPaths.TryParseCachedZipVersion(assetName, architecture, out var version))
                continue;

            return new WindowsTerminalReleaseInfo(version, assetName, downloadUrl);
        }

        throw new InvalidOperationException($"Windows Terminal latest release did not contain a {architecture} ZIP asset.");
    }

    private static string? TryGetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) ? property.GetString() : null;
}
