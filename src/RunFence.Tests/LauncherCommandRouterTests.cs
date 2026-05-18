using RunFence.Core.Ipc;
using RunFence.Launcher;
using Xunit;

namespace RunFence.Tests;

public class LauncherCommandRouterTests
{
    [Fact]
    public void Route_Resolve_UsesVerbatimRawTail()
    {
        var args = new[] { "--resolve", ".pdf", @"C:\Docs\A B.pdf" };
        var commandLine = @"""C:\RunFence\RunFence.Launcher.exe"" --resolve "".pdf"" ""C:\Docs\A B.pdf""";

        var result = LauncherCommandRouter.Route(args, commandLine);

        Assert.Equal(LauncherCommandKind.HandleAssociation, result.CommandKind);
        Assert.Equal(@"""C:\Docs\A B.pdf""", result.RawTail);
        Assert.Equal(IpcCommands.HandleAssociation, result.IpcMessage!.Command);
        Assert.Equal(".pdf", result.IpcMessage.Association);
        Assert.Equal(@"""C:\Docs\A B.pdf""", result.IpcMessage.Arguments);
    }

    [Fact]
    public void Route_LaunchApp_PreservesRawTailWithoutSplitJoinRoundTrip()
    {
        var args = new[] { "app01", @"C:\Docs\A B.pdf" };
        var commandLine = @"""C:\RunFence\RunFence.Launcher.exe"" app01 ""C:\Docs\A B.pdf\\""";

        var result = LauncherCommandRouter.Route(args, commandLine);

        Assert.Equal(LauncherCommandKind.LaunchApp, result.CommandKind);
        Assert.Equal(@"""C:\Docs\A B.pdf\\""", result.RawTail);
        Assert.Equal(result.RawTail, result.IpcMessage!.Arguments);
    }

    [Theory]
    [InlineData("--unlock", new string[0], @"""C:\RunFence\RunFence.Launcher.exe"" --unlock", null)]
    [InlineData("--load-apps", new[] { @"C:\cfg\extra config.rfn" }, @"""C:\RunFence\RunFence.Launcher.exe"" --load-apps ""C:\cfg\extra config.rfn""", @"""C:\cfg\extra config.rfn""")]
    [InlineData("--unload-apps", new[] { @"C:\cfg\extra config.rfn" }, @"""C:\RunFence\RunFence.Launcher.exe"" --unload-apps ""C:\cfg\extra config.rfn""", @"""C:\cfg\extra config.rfn""")]
    public void Route_RemovedAdminFlags_FallBackToLaunchApp(
        string appId,
        string[] extraArgs,
        string commandLine,
        string? expectedRawTail)
    {
        var args = new[] { appId }.Concat(extraArgs).ToArray();

        var result = LauncherCommandRouter.Route(args, commandLine);

        Assert.Equal(LauncherCommandKind.LaunchApp, result.CommandKind);
        Assert.Equal(expectedRawTail, result.RawTail);
        Assert.Equal(IpcCommands.Launch, result.IpcMessage!.Command);
        Assert.Equal(appId, result.IpcMessage.AppId);
        Assert.Equal(expectedRawTail, result.IpcMessage.Arguments);
    }

    [Fact]
    public void Route_UnregisterFolderHandler_ReturnsDedicatedCommandKind()
    {
        var result = LauncherCommandRouter.Route(
            ["--unregister-folder-handler"],
            @"""C:\RunFence\RunFence.Launcher.exe"" --unregister-folder-handler");

        Assert.Equal(LauncherCommandKind.UnregisterFolderHandler, result.CommandKind);
        Assert.Null(result.IpcMessage);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void Route_UnregisterFolderHandler_WithExtraArgs_ReturnsInvalid()
    {
        var result = LauncherCommandRouter.Route(
            ["--unregister-folder-handler", "--extra"],
            @"""C:\RunFence\RunFence.Launcher.exe"" --unregister-folder-handler --extra");

        Assert.Equal(LauncherCommandKind.Invalid, result.CommandKind);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage:", result.Warning);
    }
}
