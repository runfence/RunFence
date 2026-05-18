using RunFence.Core;
using RunFence.Core.Helpers;
using Xunit;

namespace RunFence.Tests;

public class AssociationCommandHelperTests
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
    public void TryMaterializeCommand_WithoutSupportedPlaceholder_AppendsQuotedArgument_ForUrlWithSpaces()
    {
        var success = AssociationCommandHelper.TryMaterializeCommand(
            @"C:\prog.exe --open",
            @"https://example.com/a path/?x=1",
            out var result,
            out var rejectionReason);

        Assert.True(success);
        Assert.Equal(string.Empty, rejectionReason);
        Assert.Equal(@"--open ""https://example.com/a path/?x=1""", result.Arguments);
    }

    [Fact]
    public void TryMaterializeCommand_WithoutSupportedPlaceholder_StillForwardsAssociationTarget()
    {
        var success = AssociationCommandHelper.TryMaterializeCommand(
            @"C:\viewer.exe --reuse-window",
            @"C:\Docs\report.pdf",
            out var result,
            out var rejectionReason);

        Assert.True(success);
        Assert.Equal(string.Empty, rejectionReason);
        Assert.False(result.UsedSupportedPlaceholder);
        Assert.Equal(@"--reuse-window C:\Docs\report.pdf", result.Arguments);
    }

    [Fact]
    public void TryMaterializeCommand_ExpandsEnvironmentVariables()
    {
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

    [Fact]
    public void TryParseRunFenceAssociationLauncherCommand_WithUnquotedPathContainingSpaces_PreservesVerbatimTail()
    {
        var commandLine = @"""C:\RunFence\RunFence.Launcher.exe"" --resolve "".pdf"" C:\Docs\My File.pdf";

        var parsed = AssociationCommandHelper.TryParseRunFenceAssociationLauncherCommand(
            commandLine,
            out var association,
            out var rawArgument);

        Assert.True(parsed);
        Assert.Equal(".pdf", association);
        Assert.Equal(@"C:\Docs\My File.pdf", rawArgument);
    }

    [Fact]
    public void TryParseRunFenceAssociationLauncherCommand_WithQuotedTailAndExtraTokens_PreservesEntireTail()
    {
        var commandLine = @"""C:\RunFence\RunFence.Launcher.exe""	--resolve	"".pdf""	""C:\Docs\My File.pdf"" --page=2   ";

        var parsed = AssociationCommandHelper.TryParseRunFenceAssociationLauncherCommand(
            commandLine,
            out var association,
            out var rawArgument);

        Assert.True(parsed);
        Assert.Equal(".pdf", association);
        Assert.Equal(@"""C:\Docs\My File.pdf"" --page=2   ", rawArgument);
    }

    [Fact]
    public void TryParseRunFenceAssociationLauncherCommand_MissingTail_ReturnsFalse()
    {
        var parsed = AssociationCommandHelper.TryParseRunFenceAssociationLauncherCommand(
            @"""C:\RunFence\RunFence.Launcher.exe"" --resolve "".pdf""   ",
            out var association,
            out var rawArgument);

        Assert.False(parsed);
        Assert.Equal(string.Empty, association);
        Assert.Equal(string.Empty, rawArgument);
    }

    [Fact]
    public void ParseAssociationTarget_WithQuotedTailAndExtraTokens_NormalizesOnlyTarget()
    {
        var normalizedTarget = AssociationCommandHelper.ParseAssociationTarget(@"""C:\Docs\My File.pdf"" --page=2");

        Assert.Equal(Path.GetFullPath(@"C:\Docs\My File.pdf"), normalizedTarget);
    }

    [Fact]
    public void ParseAssociationTarget_WithUnquotedRootedPathContainingSpaces_PreservesWholePath()
    {
        var normalizedTarget = AssociationCommandHelper.ParseAssociationTarget(@"C:\Docs\My File.pdf");

        Assert.Equal(Path.GetFullPath(@"C:\Docs\My File.pdf"), normalizedTarget);
    }

    [Fact]
    public void ParseAssociationTarget_WithQuotedRootedPathContainingAngleBracket_PreservesFileKind()
    {
        var normalizedTarget = AssociationCommandHelper.ParseAssociationTarget(@"""C:\Docs<\Bad.pdf"" --page=2");

        Assert.Equal(Path.GetFullPath(@"C:\Docs<\Bad.pdf"), normalizedTarget);
    }

    [Fact]
    public void IsRunFenceProgId_WithRunFencePrefix_ReturnsTrue()
    {
        Assert.True(AssociationCommandHelper.IsRunFenceProgId(PathConstants.HandlerProgIdPrefix + "http"));
        Assert.True(AssociationCommandHelper.IsRunFenceProgId(PathConstants.HandlerProgIdPrefix + ".pdf"));
        Assert.True(AssociationCommandHelper.IsRunFenceProgId(PathConstants.HandlerProgIdPrefix.ToUpperInvariant() + "HTTP"));
    }

    [Fact]
    public void IsRunFenceProgId_WithOtherProgId_ReturnsFalse()
    {
        Assert.False(AssociationCommandHelper.IsRunFenceProgId("ChromeHTML"));
        Assert.False(AssociationCommandHelper.IsRunFenceProgId("txtfile"));
        Assert.False(AssociationCommandHelper.IsRunFenceProgId(string.Empty));
    }

    [Fact]
    public void IsRunFenceProgId_WithNull_ReturnsFalse()
    {
        Assert.False(AssociationCommandHelper.IsRunFenceProgId(null));
    }
}
