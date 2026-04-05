using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public class PathHelperTests
{
    // --- IsPathSafeForCmd tests ---

    [Theory]
    [InlineData(@"C:\Program Files\MyApp\app.exe")]
    [InlineData(@"C:\Users\John Doe\Documents\settings.json")]
    [InlineData(@"D:\Apps (x86)\tool.exe")]
    [InlineData(@"C:\temp\file-name_v2.json")]
    [InlineData(@"C:\simple.txt")]
    [InlineData(@"C:\Users\John+Smith\settings.json")]
    [InlineData(@"C:\backup[2025]\settings.json")]
    [InlineData(@"C:\data{archive}\file.json")]
    [InlineData(@"C:\user@home\file.json")]
    [InlineData(@"C:\notes#1\file.json")]
    [InlineData(@"C:\path~backup\file.json")]
    public void IsPathSafeForCmd_ValidPaths_ReturnsTrue(string path)
    {
        Assert.True(PathHelper.IsPathSafeForCmd(path));
    }

    [Theory]
    [InlineData("C:\\test&malicious.exe")]
    [InlineData("C:\\test|pipe.exe")]
    [InlineData("C:\\test\"quote.exe")]
    [InlineData("C:\\test%env%.exe")]
    [InlineData("C:\\test^caret.exe")]
    [InlineData("C:\\test<redirect.exe")]
    [InlineData("C:\\test>redirect.exe")]
    [InlineData(@"\\server\share\file.json")]
    [InlineData(@"\\?\C:\long\path")]
    public void IsPathSafeForCmd_UnsafePaths_ReturnsFalse(string path)
    {
        Assert.False(PathHelper.IsPathSafeForCmd(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void IsPathSafeForCmd_EmptyOrNull_ReturnsFalse(string? path)
    {
        Assert.False(PathHelper.IsPathSafeForCmd(path!));
    }

    // --- IsBlockedAclPath tests ---

    [Fact]
    public void IsBlockedAclPath_WindowsDir_ReturnsTrue()
    {
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.True(PathHelper.IsBlockedAclPath(winDir));
    }

    [Fact]
    public void IsBlockedAclPath_SubpathOfWindows_ReturnsTrue()
    {
        var subPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe");
        Assert.True(PathHelper.IsBlockedAclPath(subPath));
    }

    [Theory]
    [InlineData(@"D:\MyApps\test.exe")]
    [InlineData(@"E:\Games\app.exe")]
    public void IsBlockedAclPath_NonBlockedPath_ReturnsFalse(string path)
    {
        Assert.False(PathHelper.IsBlockedAclPath(path));
    }

    // --- IsBlockedAclRoot tests ---

    [Fact]
    public void IsBlockedAclRoot_WindowsDir_ReturnsTrue()
    {
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.True(PathHelper.IsBlockedAclRoot(winDir));
    }

    [Fact]
    public void IsBlockedAclRoot_ChildOfBlockedRoot_ReturnsFalse()
    {
        var subPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe");
        Assert.False(PathHelper.IsBlockedAclRoot(subPath));
    }

    [Fact]
    public void IsBlockedAclRoot_UsersDir_ReturnsTrue()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var usersDir = Path.GetDirectoryName(userProfile);
        Assert.NotNull(usersDir);
        Assert.True(PathHelper.IsBlockedAclRoot(usersDir));
    }

    [Fact]
    public void IsBlockedAclRoot_FileUnderUsersDir_ReturnsFalse()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "test.exe");
        Assert.False(PathHelper.IsBlockedAclRoot(filePath));
    }
}