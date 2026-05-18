using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public class AppIdValidatorTests
{
    private readonly AppIdValidator _validator = new();

    [Theory]
    [InlineData("abc12")]
    [InlineData("shared-id")]
    [InlineData("APP_01")]
    [InlineData("id.with.dot")]
    public void IsValidAppId_ValidIds_ReturnsTrue(string appId)
    {
        Assert.True(_validator.IsValidAppId(appId));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(@"..\escape")]
    [InlineData(@"subdir\app")]
    [InlineData("con")]
    [InlineData("lpt1.txt")]
    public void IsValidAppId_InvalidIds_ReturnsFalse(string appId)
    {
        Assert.False(_validator.IsValidAppId(appId));
    }

    [Fact]
    public void EnsureValidAppId_InvalidId_ThrowsInvalidAppIdException()
    {
        var ex = Assert.Throws<InvalidAppIdException>(() =>
            _validator.EnsureValidAppId(@"..\bad", "Imported app ID"));

        Assert.Equal(@"..\bad", ex.AppId);
        Assert.Contains("Imported app ID", ex.Message, StringComparison.Ordinal);
    }
}
