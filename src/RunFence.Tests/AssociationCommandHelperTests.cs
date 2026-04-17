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
        _root = Registry.CurrentUser.CreateSubKey(_testSubKey)!;
    }

    public void Dispose()
    {
        _root.Dispose();
        try { Registry.CurrentUser.DeleteSubKeyTree(_testSubKey, throwOnMissingSubKey: false); }
        catch { }
    }

    // --- SubstituteArgument ---

    [Fact]
    public void SubstituteArgument_WithPercent1_ReplacesIt()
    {
        var result = AssociationCommandHelper.SubstituteArgument(@"C:\prog.exe %1", "https://example.com");

        Assert.Equal(@"C:\prog.exe https://example.com", result);
    }

    [Fact]
    public void SubstituteArgument_WithoutPercent1_Appends()
    {
        var result = AssociationCommandHelper.SubstituteArgument(@"C:\prog.exe", "https://example.com");

        Assert.Equal(@"C:\prog.exe https://example.com", result);
    }

    [Fact]
    public void SubstituteArgument_ExpandsEnvironmentVariables()
    {
        // SystemRoot is always set on Windows
        var result = AssociationCommandHelper.SubstituteArgument(@"%SystemRoot%\system32\notepad.exe", null);

        var expected = Environment.ExpandEnvironmentVariables(@"%SystemRoot%\system32\notepad.exe");
        Assert.Equal(expected, result);
        Assert.DoesNotContain("%SystemRoot%", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SubstituteArgument_NullArgs_ReplacesPercent1WithEmpty()
    {
        var result = AssociationCommandHelper.SubstituteArgument(@"C:\prog.exe %1 --end", null);

        Assert.Equal(@"C:\prog.exe  --end", result);
    }

    // --- IsRunFenceProgId ---

    [Fact]
    public void IsRunFenceProgId_WithRunFencePrefix_ReturnsTrue()
    {
        Assert.True(AssociationCommandHelper.IsRunFenceProgId("RunFence_http"));
        Assert.True(AssociationCommandHelper.IsRunFenceProgId("RunFence_.pdf"));
        Assert.True(AssociationCommandHelper.IsRunFenceProgId("RUNFENCE_HTTP")); // case-insensitive
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
        => _root.CreateSubKey(name)!;

    [Fact]
    public void RestoreFromFallback_Extension_NonEmptyFallback_RestoresDefaultValue()
    {
        // Arrange: set up .pdf key with RunFenceFallback = "Acrobat.Document.DC"
        using var key = CreateAssocKey(".pdf");
        key.SetValue(null, Constants.HandlerProgIdPrefix + ".pdf");
        key.SetValue(Constants.RunFenceFallbackValueName, "Acrobat.Document.DC");

        // Act
        var result = AssociationCommandHelper.RestoreFromFallback(key, ".pdf");

        // Assert: returned fallback value, default restored, RunFenceFallback deleted
        Assert.Equal("Acrobat.Document.DC", result);
        Assert.Equal("Acrobat.Document.DC", key.GetValue(null) as string);
        Assert.Null(key.GetValue(Constants.RunFenceFallbackValueName));
    }

    [Fact]
    public void RestoreFromFallback_Extension_EmptyFallback_DeletesOnlyDefaultValue()
    {
        // Arrange: .xyz had no previous default, so RunFenceFallback is empty string
        using var key = CreateAssocKey(".xyz");
        key.SetValue(null, Constants.HandlerProgIdPrefix + ".xyz");
        key.SetValue(Constants.RunFenceFallbackValueName, string.Empty);
        // Pre-existing sub-key like OpenWithProgids should be preserved
        using (var subKey = key.CreateSubKey("OpenWithProgids")) { }

        // Act
        var result = AssociationCommandHelper.RestoreFromFallback(key, ".xyz");

        // Assert: default value deleted (no previous), RunFenceFallback deleted, sub-key preserved
        Assert.Equal(string.Empty, result);
        Assert.Null(key.GetValue(null) as string);
        Assert.Null(key.GetValue(Constants.RunFenceFallbackValueName));
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
        key.SetValue(null, Constants.HandlerProgIdPrefix + ".pdf");
        key.SetValue(Constants.RunFenceFallbackValueName, originalProgId);
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
        key.SetValue(null, Constants.HandlerProgIdPrefix + ".txt");
        key.SetValue(Constants.RunFenceFallbackValueName, "txtfile");

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
            cmdKey.SetValue(null, $"\"{Constants.HandlerProgIdPrefix}mailto\" --resolve \"mailto\" %1");
        key.SetValue(Constants.RunFenceFallbackValueName, originalCommand);
        key.SetValue("URL Protocol", string.Empty);

        // Act
        var result = AssociationCommandHelper.RestoreFromFallback(key, "mailto");

        // Assert
        Assert.Equal(originalCommand, result);
        using var commandKey = key.OpenSubKey(@"shell\open\command");
        Assert.Equal(originalCommand, commandKey?.GetValue(null) as string);
        Assert.Null(key.GetValue(Constants.RunFenceFallbackValueName));
    }

    [Fact]
    public void RestoreFromFallback_Protocol_EmptyFallback_DeletesShellAndUrlProtocol()
    {
        // Arrange: protocol previously had no command (RunFenceFallback = "")
        using var key = CreateAssocKey("myproto");
        key.CreateSubKey(@"shell\open\command")!.Dispose();
        key.SetValue(Constants.RunFenceFallbackValueName, string.Empty);
        key.SetValue("URL Protocol", string.Empty);
        // A sibling key that should NOT be deleted
        using (key.CreateSubKey("SomeOtherKey")) { }

        // Act
        var result = AssociationCommandHelper.RestoreFromFallback(key, "myproto");

        // Assert: shell sub-tree and URL Protocol deleted, parent key preserved, sibling preserved
        Assert.Equal(string.Empty, result);
        Assert.Null(key.OpenSubKey("shell"));
        Assert.Null(key.GetValue("URL Protocol"));
        Assert.Null(key.GetValue(Constants.RunFenceFallbackValueName));
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
        key.SetValue(Constants.RunFenceFallbackValueName, "SomeProgId");
        key.SetValue(null, Constants.HandlerProgIdPrefix + ".test");

        // Act
        AssociationCommandHelper.RestoreFromFallback(key, ".test");

        // Assert: RunFenceFallback is always deleted when it existed
        Assert.Null(key.GetValue(Constants.RunFenceFallbackValueName));
    }
}
