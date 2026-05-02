using RunFence.Account.UI;
using Xunit;

namespace RunFence.Tests;

public class ProcessCommandLineFormatterTests
{
    private readonly ProcessCommandLineFormatter _formatter = new();

    [Theory]
    [InlineData("\"C:\\Program Files\\app.exe\" --flag value", "--flag value")]
    [InlineData("\"C:\\Program Files\\app.exe\"", null)]
    [InlineData("app.exe --flag value", "--flag value")]
    [InlineData("app.exe", null)]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("\"C:\\app.exe\" arg1 arg2 \"arg three\"", "arg1 arg2 \"arg three\"")]
    public void StripExecutable_ReturnsArgumentsAfterExecutable(string? commandLine, string? expected)
    {
        Assert.Equal(expected, _formatter.StripExecutable(commandLine));
    }
}
