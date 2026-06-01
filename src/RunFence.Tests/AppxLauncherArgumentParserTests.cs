using RunFence.AppxLauncher;
using Xunit;

namespace RunFence.Tests;

public sealed class AppxLauncherArgumentParserTests
{
    [Fact]
    public void TryParse_NormalArgsAndVerbatimTail_ReturnsOptionsVerbatim()
    {
        const string rawCommandLine =
            "\"RunFence.AppxLauncher.exe\" \"C:\\Logs\\appx launch.jsonl\" " +
            "\"C:\\Program Files\\WindowsApps\\OpenAI.Codex_26.527.3686.0_x64__2p2nqsd0c76g0\\app\\Codex.exe\" " +
            "codex:--resume abc";

        var success = AppxLauncherArgumentParser.TryParse(
            [
                "C:\\Logs\\appx launch.jsonl",
                "C:\\Program Files\\WindowsApps\\OpenAI.Codex_26.527.3686.0_x64__2p2nqsd0c76g0\\app\\Codex.exe",
                "codex:--resume",
                "abc"
            ],
            rawCommandLine,
            out var options,
            out var error);

        Assert.True(success);
        Assert.Equal(string.Empty, error);
        Assert.Equal("C:\\Logs\\appx launch.jsonl", options.LogFilePath);
        Assert.Equal(
            "C:\\Program Files\\WindowsApps\\OpenAI.Codex_26.527.3686.0_x64__2p2nqsd0c76g0\\app\\Codex.exe",
            options.AppxExecutablePath);
        Assert.Equal("codex:--resume abc", options.Arguments);
    }

    [Fact]
    public void TryParse_QuotedVerbatimTail_PreservesRawTail()
    {
        const string rawCommandLine =
            "\"RunFence.AppxLauncher.exe\" log.jsonl " +
            "\"C:\\Program Files\\WindowsApps\\OpenAI.Codex_26.527.3686.0_x64__2p2nqsd0c76g0\\app\\Codex.exe\" " +
            "codex:--prompt \"two words\" --json";

        var success = AppxLauncherArgumentParser.TryParse(
            [
                "log.jsonl",
                "C:\\Program Files\\WindowsApps\\OpenAI.Codex_26.527.3686.0_x64__2p2nqsd0c76g0\\app\\Codex.exe",
                "codex:--prompt",
                "two words",
                "--json"
            ],
            rawCommandLine,
            out var options,
            out var error);

        Assert.True(success);
        Assert.Equal(string.Empty, error);
        Assert.Equal("codex:--prompt \"two words\" --json", options.Arguments);
    }

    [Theory]
    [InlineData()]
    [InlineData("log.jsonl")]
    public void TryParse_TooFewParsedArguments_ReturnsFalse(params string[] args)
    {
        var success = AppxLauncherArgumentParser.TryParse(args, "\"RunFence.AppxLauncher.exe\"", out _, out var error);

        Assert.False(success);
        Assert.Contains("Expected at least 2 parsed arguments", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "C:\\Program Files\\WindowsApps\\OpenAI.Codex_26.527.3686.0_x64__2p2nqsd0c76g0\\app\\Codex.exe", "codex:")]
    [InlineData(" ", "C:\\Program Files\\WindowsApps\\OpenAI.Codex_26.527.3686.0_x64__2p2nqsd0c76g0\\app\\Codex.exe", "codex:")]
    [InlineData("log.jsonl", "", "codex:")]
    [InlineData("log.jsonl", " ", "codex:")]
    public void TryParse_WhitespaceNormalArgument_ReturnsFalse(string logFilePath, string appxExecutablePath, string arguments)
    {
        var rawCommandLine = $"\"RunFence.AppxLauncher.exe\" {logFilePath} {appxExecutablePath} {arguments}";

        var success = AppxLauncherArgumentParser.TryParse(
            [logFilePath, appxExecutablePath, arguments],
            rawCommandLine,
            out _,
            out var error);

        Assert.False(success);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void TryParse_MissingArgumentsTail_ReturnsEmptyArguments()
    {
        var success = AppxLauncherArgumentParser.TryParse(
            ["log.jsonl", "C:\\Program Files\\WindowsApps\\OpenAI.Codex_26.527.3686.0_x64__2p2nqsd0c76g0\\app\\Codex.exe"],
            "\"RunFence.AppxLauncher.exe\" log.jsonl \"C:\\Program Files\\WindowsApps\\OpenAI.Codex_26.527.3686.0_x64__2p2nqsd0c76g0\\app\\Codex.exe\"",
            out var options,
            out var error);

        Assert.True(success);
        Assert.Equal(string.Empty, error);
        Assert.Equal(string.Empty, options.Arguments);
    }
}
