using Moq;
using RunFence.Core;
using RunFence.Launch;
using RunFence.Launch.Tokens;
using Xunit;

namespace RunFence.Tests;

public class UrlValidationTests
{
    private readonly ProcessLaunchService _service;

    public UrlValidationTests()
    {
        var log = new Mock<ILoggingService>();
        _service = new ProcessLaunchService(log.Object,
            new Mock<ISplitTokenLauncher>().Object,
            new Mock<ILowIntegrityLauncher>().Object,
            new Mock<IInteractiveUserLauncher>().Object,
            new Mock<ICurrentAccountLauncher>().Object,
            new Mock<IInteractiveLogonHelper>().Object);
    }

    [Theory]
    [InlineData("steam://run/12345")]
    [InlineData("ms-settings:display")]
    [InlineData("https://example.com")]
    [InlineData("myapp://launch")]
    [InlineData("myapp://action?a=1&b=2")]
    [InlineData("steam://run/app!thing")]
    public void ValidateUrlScheme_AllowedSchemes_ReturnsTrue(string url)
    {
        var result = _service.ValidateUrlScheme(url, out var error);
        Assert.True(result);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("file://C:/test.txt")]
    [InlineData("file:///etc/passwd")]
    public void ValidateUrlScheme_FileScheme_ReturnsFalse(string url)
    {
        var result = _service.ValidateUrlScheme(url, out var error);
        Assert.False(result);
        Assert.Contains("file", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ms-msdt:something")]
    [InlineData("search-ms:something")]
    public void ValidateUrlScheme_DangerousSchemes_ReturnsFalse(string url)
    {
        var result = _service.ValidateUrlScheme(url, out var error);
        Assert.False(result);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void ValidateUrlScheme_EmptyOrNull_ReturnsFalse(string? url)
    {
        var result = _service.ValidateUrlScheme(url!, out var error);
        Assert.False(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateUrlScheme_NoColon_ReturnsFalse()
    {
        var result = _service.ValidateUrlScheme("justtext", out var error);
        Assert.False(result);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("steam://run/123\"&calc")]
    [InlineData("steam://run/%PATH%")]
    [InlineData("steam://run/123 calc")]
    [InlineData("steam://run/123\ncalc")]
    [InlineData("steam://run/123\rcalc")]
    [InlineData("steam://run/123\0calc")]
    public void ValidateUrlScheme_UnsafeCmdCharacters_ReturnsFalse(string url)
    {
        var result = _service.ValidateUrlScheme(url, out var error);
        Assert.False(result);
        Assert.Contains("unsafe", error!, StringComparison.OrdinalIgnoreCase);
    }
}