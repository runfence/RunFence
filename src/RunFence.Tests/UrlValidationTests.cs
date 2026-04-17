using RunFence.Launch;
using Xunit;

namespace RunFence.Tests;

public class UrlValidationTests
{
    [Theory]
    [InlineData("steam://run/12345")]
    [InlineData("ms-settings:display")]
    [InlineData("https://example.com")]
    [InlineData("myapp://launch")]
    [InlineData("myapp://action?a=1&b=2")]
    [InlineData("steam://run/app!thing")]
    [InlineData("steam://run/%PATH%")]
    [InlineData("steam://run/123 calc")]
    [InlineData("https://example.com/path%20name")]
    public void ValidateUrlScheme_AllowedSchemes_ReturnsTrue(string url)
    {
        var result = ProcessLaunchHelper.ValidateUrlScheme(url, out var error);
        Assert.True(result);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("file://C:/test.txt")]
    [InlineData("file:///etc/passwd")]
    public void ValidateUrlScheme_FileScheme_ReturnsFalse(string url)
    {
        var result = ProcessLaunchHelper.ValidateUrlScheme(url, out var error);
        Assert.False(result);
        Assert.Contains("file", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ms-msdt:something")]
    [InlineData("search-ms:something")]
    public void ValidateUrlScheme_DangerousSchemes_ReturnsFalse(string url)
    {
        var result = ProcessLaunchHelper.ValidateUrlScheme(url, out var error);
        Assert.False(result);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    [InlineData("justtext")]
    public void ValidateUrlScheme_EmptyNullOrNoScheme_ReturnsFalse(string? url)
    {
        var result = ProcessLaunchHelper.ValidateUrlScheme(url!, out var error);
        Assert.False(result);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("steam://run/123\"&calc")]
    [InlineData("steam://run/123\ncalc")]
    [InlineData("steam://run/123\rcalc")]
    [InlineData("steam://run/123\0calc")]
    public void ValidateUrlScheme_UnsafeCharacters_ReturnsFalse(string url)
    {
        var result = ProcessLaunchHelper.ValidateUrlScheme(url, out var error);
        Assert.False(result);
        Assert.Contains("unsafe", error!, StringComparison.OrdinalIgnoreCase);
    }
}
