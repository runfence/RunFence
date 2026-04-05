using RunFence.Account.UI;
using Xunit;

namespace RunFence.Tests;

public class AccountGridProcessExpanderTests
{
    [Theory]
    // Quoted exe path with args
    [InlineData("\"C:\\Program Files\\app.exe\" --flag value", "--flag value")]
    // Quoted exe path with no args
    [InlineData("\"C:\\Program Files\\app.exe\"", null)]
    // Unquoted exe with space-separated args
    [InlineData("app.exe --flag value", "--flag value")]
    // Unquoted exe with no args
    [InlineData("app.exe", null)]
    // Null input
    [InlineData(null, null)]
    // Empty string
    [InlineData("", null)]
    // Quoted path, multiple args
    [InlineData("\"C:\\app.exe\" arg1 arg2 \"arg three\"", "arg1 arg2 \"arg three\"")]
    public void StripExeFromCommandLine_ReturnsArgsAfterExe(string? cmdLine, string? expected)
    {
        Assert.Equal(expected, AccountGridProcessExpander.StripExeFromCommandLine(cmdLine));
    }
}