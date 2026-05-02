using Microsoft.Win32;
using RunFence.Core;
using RunFence.Core.Helpers;
using Xunit;

namespace RunFence.Tests;

public class AssociationCommandHelperTests : IDisposable
{
    private readonly string _testSubKey;
    private readonly RegistryKey _root;

    public AssociationCommandHelperTests()
    {
        _testSubKey = $@"Software\RunFenceTests\CmdHelper_{Guid.NewGuid():N}";
        _root = Registry.CurrentUser.CreateSubKey(_testSubKey);
    }

    public void Dispose()
    {
        _root.Dispose();
        try { Registry.CurrentUser.DeleteSubKeyTree(_testSubKey, throwOnMissingSubKey: false); }
        catch { }
    }

    // --- Materialization ---

    [Theory]
    [InlineData("%1")]
    [InlineData("%L")]
    [InlineData("%l")]
    [InlineData("%V")]
    [InlineData("%v")]
    [InlineData("%U")]
    [InlineData("%u")]
    [InlineData("%*")]
    public void TryMaterializeCommand_WithSupportedPlaceholder_ReplacesItWithoutAppendingDuplicate(string placeholder)
    {
        var success = AssociationCommandHelper.TryMaterializeCommand(
            $@"C:\prog.exe --first={placeholder} --second={placeholder}",
            "https://example.com",
            out var result,
            out var rejectionReason);

        Assert.True(success);
        Assert.Equal(string.Empty, rejectionReason);
        Assert.True(result.UsedSupportedPlaceholder);
        Assert.Equal(@"C:\prog.exe", result.ExePath);
        Assert.Equal(@"--first=https://example.com --second=https://example.com", result.Arguments);
        Assert.Equal(@"C:\prog.exe --first=https://example.com --second=https://example.com", result.MaterializedCommand);
    }

    [Fact]
    public void TryMaterializeCommand_WithoutSupportedPlaceholder_AppendsQuotedArgument()
    {
        var success = AssociationCommandHelper.TryMaterializeCommand(
            @"C:\prog.exe --open",
            @"C:\Docs\My File.pdf",
            out var result,
            out var rejectionReason);

        Assert.True(success);
        Assert.Equal(string.Empty, rejectionReason);
        Assert.False(result.UsedSupportedPlaceholder);
        Assert.Equal(@"C:\prog.exe", result.ExePath);
        Assert.Equal(@"--open ""C:\Docs\My File.pdf""", result.Arguments);
    }

    [Fact]
    public void TryMaterializeCommand_ExpandsEnvironmentVariables()
    {
        // SystemRoot is always set on Windows
        var success = AssociationCommandHelper.TryMaterializeCommand(
            @"%SystemRoot%\system32\notepad.exe",
            null,
            out var result,
            out var rejectionReason);

        var expected = Environment.ExpandEnvironmentVariables(@"%SystemRoot%\system32\notepad.exe");
        Assert.True(success);
        Assert.Equal(string.Empty, rejectionReason);
        Assert.Equal(expected, result.MaterializedCommand);
        Assert.DoesNotContain("%SystemRoot%", result.MaterializedCommand, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryMaterializeCommand_NullArgs_ReplacesSupportedPlaceholderWithEmpty()
    {
        var success = AssociationCommandHelper.TryMaterializeCommand(
            @"C:\prog.exe %1 --end",
            null,
            out var result,
            out var rejectionReason);

        Assert.True(success);
        Assert.Equal(string.Empty, rejectionReason);
        Assert.Equal(@"--end", result.Arguments);
    }

    [Fact]
    public void TryMaterializeCommand_UnsupportedAssociationPlaceholder_IsRejected()
    {
        var success = AssociationCommandHelper.TryMaterializeCommand(
            @"C:\prog.exe %2",
            "https://example.com",
            out _,
            out var rejectionReason);

        Assert.False(success);
        Assert.Equal("command contains unsupported association placeholder '%2'", rejectionReason);
    }

    // --- IsRunFenceProgId ---

    [Fact]
    public void IsRunFenceProgId_WithRunFencePrefix_ReturnsTrue()
    {
        Assert.True(AssociationCommandHelper.IsRunFenceProgId(PathConstants.HandlerProgIdPrefix + "http"));
        Assert.True(AssociationCommandHelper.IsRunFenceProgId(PathConstants.HandlerProgIdPrefix + ".pdf"));
        Assert.True(AssociationCommandHelper.IsRunFenceProgId(PathConstants.HandlerProgIdPrefix.ToUpperInvariant() + "HTTP")); // case-insensitive
    }

    [Fact]
    public void IsRunFenceProgId_WithOtherProgId_ReturnsFalse()
    {
        Assert.False(AssociationCommandHelper.IsRunFenceProgId("ChromeHTML"));
        Assert.False(AssociationCommandHelper.IsRunFenceProgId("txtfile"));
        Assert.False(AssociationCommandHelper.IsRunFenceProgId(""));
    }

    [Fact]
    public void IsRunFenceProgId_WithNull_ReturnsFalse()
    {
        Assert.False(AssociationCommandHelper.IsRunFenceProgId(null));
    }

    // --- RestoreFromFallback ---

    private RegistryKey CreateAssocKey(string name)
        => _root.CreateSubKey(name);

    [Fact]
    public void RestoreFromFallback_Extension_NonEmptyFallback_RestoresDefaultValue()
    {
        // Arrange: set up .pdf key with RunFenceFallback = "Acrobat.Document.DC"
        using var key = CreateAssocKey(".pdf");
        key.SetValue(null, PathConstants.HandlerProgIdPrefix + ".pdf");
        key.SetValue(PathConstants.RunFenceFallbackValueName, "Acrobat.Document.DC");

        // Act
        var result = AssociationCommandHelper.RestoreFromFallback(key, ".pdf");

        // Assert: returned fallback value, default restored, RunFenceFallback deleted
        Assert.Equal("Acrobat.Document.DC", result);
        Assert.Equal("Acrobat.Document.DC", key.GetValue(null) as string);
        Assert.Null(key.GetValue(PathConstants.RunFenceFallbackValueName));
    }

    [Fact]
    public void RestoreFromFallback_Extension_EmptyFallback_DeletesOnlyDefaultValue()
    {
        // Arrange: .xyz had no previous default, so RunFenceFallback is empty string
        using var key = CreateAssocKey(".xyz");
        key.SetValue(null, PathConstants.HandlerProgIdPrefix + ".xyz");
        key.SetValue(PathConstants.RunFenceFallbackValueName, string.Empty);
        // Pre-existing sub-key like OpenWithProgids should be preserved
        using (key.CreateSubKey("OpenWithProgids")) { }

        // Act
        var result = AssociationCommandHelper.RestoreFromFallback(key, ".xyz");

        // Assert: default value deleted (no previous), RunFenceFallback deleted, sub-key preserved
        Assert.Equal(string.Empty, result);
        Assert.Null(key.GetValue(null) as string);
        Assert.Null(key.GetValue(PathConstants.RunFenceFallbackValueName));
        using var subKeyAfter = key.OpenSubKey("OpenWithProgids");
        Assert.NotNull(subKeyAfter);
    }

    [Fact]
    public void RestoreFromFallback_Extension_CleansUpShellOpenCommandAddedByDirectHandler()
    {
        // Arrange: extension key had a command-based direct handler registered;
        // RunFenceFallback stores original default ProgId; shell\open\command was added by RunFence.
        const string originalProgId = "Acrobat.Document.DC";
        using var key = CreateAssocKey(".pdf");
        key.SetValue(null, PathConstants.HandlerProgIdPrefix + ".pdf");
        key.SetValue(PathConstants.RunFenceFallbackValueName, originalProgId);
        using (var cmdKey = key.CreateSubKey(@"shell\open\command"))
            cmdKey.SetValue(null, @"""C:\custom.exe"" ""%1""");
        // Add shell\edit sibling to verify it is preserved
        using (var editKey = key.CreateSubKey(@"shell\edit\command"))
            editKey.SetValue(null, "notepad.exe");

        // Act
        var result = AssociationCommandHelper.RestoreFromFallback(key, ".pdf");

        // Assert: default restored
        Assert.Equal(originalProgId, result);
        Assert.Equal(originalProgId, key.GetValue(null) as string);

        // Assert: shell\open\command removed (and shell\open cleaned up since it was empty)
        Assert.Null(key.OpenSubKey(@"shell\open\command"));
        Assert.Null(key.OpenSubKey(@"shell\open"));

        // Assert: shell\edit preserved (shell preserved because sibling exists)
        using var shellKey = key.OpenSubKey("shell");
        Assert.NotNull(shellKey);
        using var editCmdKey = key.OpenSubKey(@"shell\edit\command");
        Assert.NotNull(editCmdKey);
    }

    [Fact]
    public void RestoreFromFallback_Extension_NoShellCommand_NoCleanupNeeded()
    {
        // Arrange: extension key with only a default value and RunFenceFallback — no shell\open\command
        using var key = CreateAssocKey(".txt");
        key.SetValue(null, PathConstants.HandlerProgIdPrefix + ".txt");
        key.SetValue(PathConstants.RunFenceFallbackValueName, "txtfile");

        // Act
        var result = AssociationCommandHelper.RestoreFromFallback(key, ".txt");

        // Assert: works fine, default restored, no crash
        Assert.Equal("txtfile", result);
        Assert.Equal("txtfile", key.GetValue(null) as string);
        Assert.Null(key.OpenSubKey("shell"));
    }

    [Fact]
    public void RestoreFromFallback_Protocol_NonEmptyFallback_RestoresCommand()
    {
        // Arrange: mailto protocol previously had a command
        const string originalCommand = @"""C:\Mail\client.exe"" %1";
        using var key = CreateAssocKey("mailto");
        using (var cmdKey = key.CreateSubKey(@"shell\open\command"))
            cmdKey.SetValue(null, $"\"{PathConstants.HandlerProgIdPrefix}mailto\" --resolve \"mailto\" %1");
        key.SetValue(PathConstants.RunFenceFallbackValueName, originalCommand);
        key.SetValue("URL Protocol", string.Empty);

        // Act
        var result = AssociationCommandHelper.RestoreFromFallback(key, "mailto");

        // Assert
        Assert.Equal(originalCommand, result);
        using var commandKey = key.OpenSubKey(@"shell\open\command");
        Assert.Equal(originalCommand, commandKey?.GetValue(null) as string);
        Assert.Null(key.GetValue(PathConstants.RunFenceFallbackValueName));
    }

    [Fact]
    public void RestoreFromFallback_Protocol_EmptyFallback_DeletesShellAndUrlProtocol()
    {
        // Arrange: protocol previously had no command (RunFenceFallback = "")
        using var key = CreateAssocKey("myproto");
        key.CreateSubKey(@"shell\open\command").Dispose();
        key.SetValue(PathConstants.RunFenceFallbackValueName, string.Empty);
        key.SetValue("URL Protocol", string.Empty);
        // A sibling key that should NOT be deleted
        using (key.CreateSubKey("SomeOtherKey")) { }

        // Act
        var result = AssociationCommandHelper.RestoreFromFallback(key, "myproto");

        // Assert: shell sub-tree and URL Protocol deleted, parent key preserved, sibling preserved
        Assert.Equal(string.Empty, result);
        Assert.Null(key.OpenSubKey("shell"));
        Assert.Null(key.GetValue("URL Protocol"));
        Assert.Null(key.GetValue(PathConstants.RunFenceFallbackValueName));
        using var siblingKey = key.OpenSubKey("SomeOtherKey");
        Assert.NotNull(siblingKey); // parent key preserved with siblings
    }

    [Fact]
    public void RestoreFromFallback_NoFallbackValue_ReturnsNull()
    {
        // Arrange: key without RunFenceFallback
        using var key = CreateAssocKey(".doc");
        key.SetValue(null, "Word.Document.12");

        // Act
        var result = AssociationCommandHelper.RestoreFromFallback(key, ".doc");

        // Assert: returns null, no changes
        Assert.Null(result);
        Assert.Equal("Word.Document.12", key.GetValue(null) as string);
    }

    [Fact]
    public void RestoreFromFallback_DeletesRunFenceFallbackValue()
    {
        // Arrange
        using var key = CreateAssocKey(".test");
        key.SetValue(PathConstants.RunFenceFallbackValueName, "SomeProgId");
        key.SetValue(null, PathConstants.HandlerProgIdPrefix + ".test");

        // Act
        AssociationCommandHelper.RestoreFromFallback(key, ".test");

        // Assert: RunFenceFallback is always deleted when it existed
        Assert.Null(key.GetValue(PathConstants.RunFenceFallbackValueName));
    }
}
