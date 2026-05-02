using RunFence.Launcher;
using Xunit;

namespace RunFence.Tests;

public class AssociationFallbackHelperTests
{
    [Theory]
    [InlineData("%1")]
    [InlineData("%L")]
    [InlineData("%l")]
    [InlineData("%V")]
    [InlineData("%v")]
    [InlineData("%U")]
    [InlineData("%u")]
    [InlineData("%*")]
    public void TryBuildProcessStartInfo_WithSupportedPlaceholder_ReplacesItWithoutAppendingDuplicate(string placeholder)
    {
        var success = AssociationFallbackHelper.TryBuildProcessStartInfo(
            $@"""C:\Apps\browser.exe"" --first=""{placeholder}"" --second=""{placeholder}""",
            "https://example.com/path?q=1",
            out var startInfo,
            out var errorMessage);

        Assert.True(success);
        Assert.Equal(string.Empty, errorMessage);
        Assert.Equal(@"C:\Apps\browser.exe", startInfo.FileName);
        Assert.Equal(@"--first=""https://example.com/path?q=1"" --second=""https://example.com/path?q=1""", startInfo.Arguments);
        Assert.True(startInfo.UseShellExecute);
    }

    [Fact]
    public void TryBuildProcessStartInfo_WithoutSupportedPlaceholder_AppendsQuotedArgument()
    {
        var success = AssociationFallbackHelper.TryBuildProcessStartInfo(
            @"""C:\Apps\viewer.exe"" --open",
            @"C:\Docs\My File.pdf",
            out var startInfo,
            out var errorMessage);

        Assert.True(success);
        Assert.Equal(string.Empty, errorMessage);
        Assert.Equal(@"C:\Apps\viewer.exe", startInfo.FileName);
        Assert.Equal(@"--open ""C:\Docs\My File.pdf""", startInfo.Arguments);
        Assert.True(startInfo.UseShellExecute);
    }

    [Fact]
    public void TryBuildProcessStartInfo_UnsupportedAssociationPlaceholder_IsRejected()
    {
        var success = AssociationFallbackHelper.TryBuildProcessStartInfo(
            @"""C:\Apps\viewer.exe"" ""%2""",
            @"C:\Docs\report.pdf",
            out _,
            out var errorMessage);

        Assert.False(success);
        Assert.Equal("Failed to launch fallback handler: command contains unsupported association placeholder '%2'", errorMessage);
    }
}
