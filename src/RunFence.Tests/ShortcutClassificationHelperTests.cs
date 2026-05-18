using RunFence.Apps.Shortcuts;
using Xunit;

namespace RunFence.Tests;

public class ShortcutClassificationHelperTests
{
    // --- IsFolderShortcutTarget ---

    [Theory]
    [InlineData(null, null, @"C:\Users\alice\Documents", false)]
    [InlineData(null, @"C:\Users\alice\Documents", @"C:\Users\alice\Documents", false)]
    [InlineData(@"C:\Users\alice\Documents", null, @"C:\Users\alice\Documents", true)]
    [InlineData(@"C:\Users\alice\Documents\", null, @"C:\Users\alice\Documents", true)] // trailing separator stripped
    [InlineData(@"C:\Users\alice\Documents", null, @"C:\Users\alice\OTHER", false)]
    [InlineData(@"C:\windows\explorer.exe", @"C:\Users\alice\Documents", @"C:\Users\alice\Documents", true)]
    [InlineData(@"C:\windows\explorer.exe", @"/separate,C:\Users\alice\Documents", @"C:\Users\alice\Documents", true)]
    [InlineData(@"C:\windows\explorer.exe", @"C:\Users\alice\OTHER", @"C:\Users\alice\Documents", false)]
    [InlineData(@"C:\windows\explorer.exe", null, @"C:\Users\alice\Documents", false)]
    [InlineData(@"C:\Program Files\App\app.exe", @"C:\Users\alice\Documents", @"C:\Users\alice\Documents", false)]
    public void IsFolderShortcutTarget_ReturnsExpected(
        string? target,
        string? args,
        string normalizedFolder,
        bool expected)
    {
        var result = ShortcutClassificationHelper.IsFolderShortcutTarget(target, args, normalizedFolder);

        Assert.Equal(expected, result);
    }

    // Case insensitivity for direct path match and explorer.exe args match

    [Theory]
    [InlineData(@"C:\Users\Alice\Documents", @"c:\users\alice\documents")] // target case mismatch
    [InlineData(@"c:\users\alice\documents", @"C:\Users\Alice\Documents")] // normalizedFolder case mismatch
    public void IsFolderShortcutTarget_DirectMatch_IsCaseInsensitive(string target, string normalizedFolder)
    {
        var result = ShortcutClassificationHelper.IsFolderShortcutTarget(target, args: null, normalizedFolder);

        Assert.True(result);
    }

    [Fact]
    public void IsFolderShortcutTarget_ExplorerArgMatch_IsCaseInsensitive()
    {
        const string target = @"C:\windows\Explorer.EXE";
        const string args = @"C:\Users\ALICE\Documents";
        const string normalizedFolder = @"C:\Users\alice\Documents";

        var result = ShortcutClassificationHelper.IsFolderShortcutTarget(target, args, normalizedFolder);

        Assert.True(result);
    }

    [Theory]
    [InlineData(@"C:\windows\explorer.exe", "/select,\"C:\\Users\\alice\\Documents\\report.txt\"", @"C:\Users\alice\Documents")]
    [InlineData(@"C:\windows\explorer.exe", "/e,/root,\"C:\\Users\\alice\\Documents2\"", @"C:\Users\alice\Documents")]
    [InlineData(@"C:\windows\explorer.exe", @"/e,/root,C:\Users\alice\Documents2", @"C:\Users\alice\Documents")]
    public void IsFolderShortcutTarget_DoesNotUseSubstringMatching(string target, string args, string normalizedFolder)
    {
        Assert.False(ShortcutClassificationHelper.IsFolderShortcutTarget(target, args, normalizedFolder));
    }

    [Theory]
    [InlineData("/e,/root,\"C:\\Users\\alice\\Documents\"", @"C:\Users\alice\Documents")]
    [InlineData(@"/e,/root,C:\Users\alice\Documents", @"C:\Users\alice\Documents")]
    [InlineData(@"""C:\Users\alice\Documents""", @"C:\Users\alice\Documents")]
    public void IsFolderShortcutTarget_ExplorerPathOperandMustMatchExactly(string args, string normalizedFolder)
    {
        Assert.True(ShortcutClassificationHelper.IsFolderShortcutTarget(
            @"C:\windows\explorer.exe",
            args,
            normalizedFolder));
    }

    [Theory]
    [InlineData("app-id", "app-id", "")]
    [InlineData("app-id --flag value", "app-id", "--flag value")]
    [InlineData("\"app-id\" \"C:\\Folder Path\"", "app-id", "\"C:\\Folder Path\"")]
    [InlineData("\"app-id\" \"C:\\Folder Path\" tail", "app-id", "\"C:\\Folder Path\" tail")]
    public void ParseManagedShortcutArgs_ParsesFirstCommandLineOperand(string currentArgs, string appId, string expected)
    {
        Assert.Equal(expected, ShortcutClassificationHelper.ParseManagedShortcutArgs(currentArgs, appId));
    }

    [Theory]
    [InlineData("app-id-2 --flag", "app-id")]
    [InlineData("\"app-id-2\" --flag", "app-id")]
    [InlineData("--flag app-id", "app-id")]
    public void ParseManagedShortcutArgs_DoesNotUseSubstringPrefixMatching(string currentArgs, string appId)
    {
        Assert.Null(ShortcutClassificationHelper.ParseManagedShortcutArgs(currentArgs, appId));
    }

    [Fact]
    public void ExcludeSystemExecutables_RemovesWindowsDirectoryExecutablesFromDiscoverResults()
    {
        var apps = new List<RunFence.Core.Models.DiscoveredApp>
        {
            new("App", @"C:\Apps\App.exe"),
            new("Cmd", @"C:\Windows\System32\cmd.exe"),
            new("Shell", @"C:\Windows\explorer.exe")
        };

        var filtered = ShortcutClassificationHelper.ExcludeSystemExecutables(apps);

        Assert.Collection(
            filtered,
            app =>
            {
                Assert.Equal("App", app.Name);
                Assert.Equal(@"C:\Apps\App.exe", app.TargetPath);
            });
    }
}
