using RunFence.Launch;
using Xunit;

namespace RunFence.Tests;

public sealed class FolderHandlerRegistryPathMapperTests
{
    private const string AccountSid = "S-1-5-21-100-200-300-400";

    [Fact]
    public void NormalizeValueName_DefaultValue_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, FolderHandlerRegistryPathMapper.NormalizeValueName(null));
    }

    [Fact]
    public void NormalizeValueName_NamedValue_ReturnsOriginalName()
    {
        Assert.Equal("DelegateExecute", FolderHandlerRegistryPathMapper.NormalizeValueName("DelegateExecute"));
    }

    [Fact]
    public void BuildFullPath_SoftwareRoot_UsesAccountRoot()
    {
        Assert.Equal(
            $@"{AccountSid}\Software",
            FolderHandlerRegistryPathMapper.BuildFullPath(AccountSid, "Software"));
    }

    [Fact]
    public void BuildFullPath_SoftwareSubkey_UsesAccountRoot()
    {
        Assert.Equal(
            $@"{AccountSid}\Software\Microsoft\Windows",
            FolderHandlerRegistryPathMapper.BuildFullPath(AccountSid, @"Software\Microsoft\Windows"));
    }

    [Fact]
    public void BuildFullPath_ClassKey_UsesSoftwareClassesFallback()
    {
        Assert.Equal(
            $@"{AccountSid}\Software\Classes\Directory\shell\open\command",
            FolderHandlerRegistryPathMapper.BuildFullPath(AccountSid, @"Directory\shell\open\command"));
    }
}
