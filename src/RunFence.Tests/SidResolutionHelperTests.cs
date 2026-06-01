using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public class SidResolutionHelperTests
{
    private const string OtherSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

    // --- IsSystemSid ---

    [Theory]
    [InlineData("S-1-5-18")]
    [InlineData("s-1-5-18")]
    public void IsSystemSid_SystemSid_ReturnsTrue(string sid)
    {
        Assert.True(SidResolutionHelper.IsSystemSid(sid));
    }

    // --- CanLaunchWithoutPassword ---

    [Fact]
    public void CanLaunchWithoutPassword_SystemSid_ReturnsTrue()
    {
        Assert.True(SidResolutionHelper.CanLaunchWithoutPassword(SidConstants.SystemSid));
    }

    [Fact]
    public void CanLaunchWithoutPassword_CurrentUserSid_ReturnsTrue()
    {
        Assert.True(SidResolutionHelper.CanLaunchWithoutPassword(SidResolutionHelper.GetCurrentUserSid()));
    }

    [Fact]
    public void CanLaunchWithoutPassword_OtherSid_ReturnsFalse()
    {
        // A random SID that is neither current user, interactive user, nor SYSTEM
        Assert.False(SidResolutionHelper.CanLaunchWithoutPassword(OtherSid));
    }

    [Fact]
    public void CanLaunchWithoutPassword_Null_ReturnsFalse()
    {
        Assert.False(SidResolutionHelper.CanLaunchWithoutPassword(null));
    }
}
