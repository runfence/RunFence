using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public sealed class RootedLocalPathNormalizerTests
{
    [Fact]
    public void TryNormalizeRootedLocalPath_DriveRootedPath_ReturnsFullPathWithoutBoundaryTrim()
    {
        var normalized = RootedLocalPathNormalizer.TryNormalizeRootedLocalPath(
            @"C:\Apps\Vendor\..\Vendor\App\",
            out var path);

        Assert.True(normalized);
        Assert.Equal(@"C:\Apps\Vendor\App\", path);
    }

    [Fact]
    public void TryNormalizeRootedLocalBoundaryPath_TrimsTrailingSeparators()
    {
        var normalized = RootedLocalPathNormalizer.TryNormalizeRootedLocalBoundaryPath(
            @"C:\Apps\Vendor\App\\",
            out var path);

        Assert.True(normalized);
        Assert.Equal(@"C:\Apps\Vendor\App", path);
    }

    [Theory]
    [InlineData(@"tool.exe")]
    [InlineData(@"C:tool.exe")]
    [InlineData(@"\tool.exe")]
    [InlineData(@"/tool.exe")]
    [InlineData(@"https://example.com/tool.exe")]
    [InlineData(@"shell:AppsFolder")]
    [InlineData(@"\\server\share\tool.exe")]
    [InlineData(@"\\?\C:\Apps\tool.exe")]
    [InlineData(@"\\.\C:\Apps\tool.exe")]
    [InlineData(@"//?/C:/Apps/tool.exe")]
    public void TryNormalizeRootedLocalPath_InvalidKinds_ReturnFalse(string path)
    {
        Assert.False(RootedLocalPathNormalizer.TryNormalizeRootedLocalPath(path, out _));
        Assert.False(RootedLocalPathNormalizer.TryNormalizeRootedLocalBoundaryPath(path, out _));
    }

    [Fact]
    public void TryNormalizeRootedLocalPath_MalformedString_ReturnsFalse()
    {
        Assert.False(RootedLocalPathNormalizer.TryNormalizeRootedLocalPath("C:\\bad\0tool.exe", out _));
    }
}
