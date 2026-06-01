using RunFence.Launch;
using Xunit;

namespace RunFence.Tests;

public class LaunchFileExtensionRulesTests
{
    [Theory]
    [InlineData(".exe")]
    [InlineData(".com")]
    [InlineData(".scr")]
    [InlineData(".pif")]
    [InlineData(".cpl")]
    public void IsDirectExecutableExtension_PreservesExeExtensions(string extension)
    {
        Assert.True(LaunchFileExtensionRules.IsDirectExecutableExtension(extension));
        Assert.True(LaunchFileExtensionRules.CanLaunchDirectExtension(extension));
        Assert.True(LaunchFileExtensionRules.IsSupportedHandlerSuggestionExtension(extension));
    }

    [Theory]
    [InlineData(".cmd")]
    [InlineData(".bat")]
    public void IsScriptExtension_PreservesCmdBatExtensions(string extension)
    {
        Assert.True(LaunchFileExtensionRules.IsCmdScriptExtension(extension));
        Assert.False(LaunchFileExtensionRules.CanLaunchDirectExtension(extension));
        Assert.True(LaunchFileExtensionRules.IsSupportedHandlerSuggestionExtension(extension));
        Assert.True(LaunchFileExtensionRules.IsSupportedHandlerSuggestionExtension(extension.ToUpperInvariant()));
    }

    [Fact]
    public void IsPowerShellScriptExtension_RecognizesPs1Extension()
    {
        Assert.True(LaunchFileExtensionRules.IsPowerShellScriptExtension(".ps1"));
        Assert.False(LaunchFileExtensionRules.IsCmdScriptExtension(".ps1"));
        Assert.False(LaunchFileExtensionRules.IsDirectExecutableExtension(".ps1"));
        Assert.True(LaunchFileExtensionRules.IsSupportedHandlerSuggestionExtension(".ps1"));
    }

}
