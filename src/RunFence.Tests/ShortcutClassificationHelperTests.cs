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
}
