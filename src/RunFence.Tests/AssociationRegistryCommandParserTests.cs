using RunFence.Core.Helpers;
using Xunit;

namespace RunFence.Tests;

public class AssociationRegistryCommandParserTests
{
    // IsDefaultArgs

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("%1", true)]
    [InlineData("\"%1\"", true)]
    [InlineData("-- \"%1\"", false)]
    [InlineData("--open %1", false)]
    public void IsDefaultArgs_VariousInputs_ReturnsExpected(string? args, bool expected) =>
        Assert.Equal(expected, AssociationRegistryCommandParser.IsDefaultArgs(args));

    // ExtractExeFromCommand

    [Fact]
    public void ExtractExeFromCommand_QuotedPath_ReturnsUnquotedPath() =>
        Assert.Equal(@"C:\app.exe", AssociationRegistryCommandParser.ExtractExeFromCommand(@"""C:\app.exe"" %1"));

    [Fact]
    public void ExtractExeFromCommand_UnquotedPath_ReturnsPath() =>
        Assert.Equal(@"C:\app.exe", AssociationRegistryCommandParser.ExtractExeFromCommand(@"C:\app.exe %1"));

    [Fact]
    public void ExtractExeFromCommand_Null_ReturnsNull() =>
        Assert.Null(AssociationRegistryCommandParser.ExtractExeFromCommand(null));

    // ExtractNonDefaultArgs

    [Fact]
    public void ExtractNonDefaultArgs_OnlyPercent1_ReturnsNull() =>
        Assert.Null(AssociationRegistryCommandParser.ExtractNonDefaultArgs(@"""C:\app.exe"" %1"));

    [Fact]
    public void ExtractNonDefaultArgs_WithDashDashArgs_ReturnsRemainder()
    {
        var result = AssociationRegistryCommandParser.ExtractNonDefaultArgs(@"""C:\app.exe"" -- ""%1""");
        Assert.Equal("-- \"%1\"", result);
    }

    [Fact]
    public void ExtractNonDefaultArgs_Null_ReturnsNull() =>
        Assert.Null(AssociationRegistryCommandParser.ExtractNonDefaultArgs(null));
}
