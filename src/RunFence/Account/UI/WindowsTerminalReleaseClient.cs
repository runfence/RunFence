using System.Net.Http.Headers;
using System.Runtime.InteropServices;

namespace RunFence.Account.UI;

public interface IWindowsTerminalReleaseClient
{
    Task<WindowsTerminalReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken);
}

public interface IWindowsTerminalReleaseHttpTransport
{
    Task<string> GetLatestReleaseJsonAsync(CancellationToken cancellationToken);
}

public sealed class WindowsTerminalReleaseClient(
    IWindowsTerminalReleaseHttpTransport httpTransport,
    WindowsTerminalReleaseParser releaseParser)
    : IWindowsTerminalReleaseClient
{
    public async Task<WindowsTerminalReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        var responseJson = await httpTransport.GetLatestReleaseJsonAsync(cancellationToken).ConfigureAwait(false);
        var architecture = GetArchitectureSuffix();
        return releaseParser.ParseLatestRelease(responseJson, architecture);
    }

    public static string GetArchitectureSuffix()
        => RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            _ => throw new NotSupportedException($"Windows Terminal shared deployment does not support {RuntimeInformation.OSArchitecture}.")
        };
}

public sealed class WindowsTerminalReleaseHttpTransport : IWindowsTerminalReleaseHttpTransport
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/microsoft/terminal/releases/latest";
    private readonly HttpClient _httpClient = CreateHttpClient();

    public async Task<string> GetLatestReleaseJsonAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RunFence", "1.0"));
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return httpClient;
    }
}
