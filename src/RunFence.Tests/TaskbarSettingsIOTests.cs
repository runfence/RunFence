using PrefTrans.Services.IO;
using Xunit;

namespace RunFence.Tests;

public class TaskbarSettingsIOTests
{
    [Theory]
    [InlineData(@"..\escape.lnk")]
    [InlineData(@"folder\escape.lnk")]
    [InlineData(@"C:\Windows\notepad.lnk")]
    [InlineData(@"\\server\share\tool.lnk")]
    [InlineData(@"bad.txt")]
    [InlineData(@"bad|name.lnk")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolvePinnedShortcutDestinationPath_InvalidName_ReturnsFalse(string importedName)
    {
        var taskbarFolder = @"C:\Users\Test\AppData\Roaming\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar";

        var result = TaskbarSettingsIO.TryResolvePinnedShortcutDestinationPath(taskbarFolder, importedName, out var destinationPath);

        Assert.False(result);
        Assert.Equal(string.Empty, destinationPath);
    }

    [Fact]
    public void TryResolvePinnedShortcutDestinationPath_ValidShortcut_ReturnsPathUnderTaskbarFolder()
    {
        var taskbarFolder = @"C:\Users\Test\AppData\Roaming\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar";

        var result = TaskbarSettingsIO.TryResolvePinnedShortcutDestinationPath(taskbarFolder, "Claude Code.lnk", out var destinationPath);

        Assert.True(result);
        Assert.Equal(Path.Combine(taskbarFolder, "Claude Code.lnk"), destinationPath);
    }
}
