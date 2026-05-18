using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public class CommandLineHelperTests
{
    // --- SliceVerbatimTail / SkipArgs ---

    [Theory]
    // Preserves exact original quoting — the core invariant
    [InlineData("\"exe\" appid a b \"c d\" \"e\"", 2, "a b \"c d\" \"e\"")]
    [InlineData("exe appid a b \"c d\" \"e\"", 2, "a b \"c d\" \"e\"")]
    [InlineData("exe\tappid\t\"a\tb\"\tc", 2, "\"a\tb\"\tc")]
    // Single extra arg
    [InlineData("\"exe\" appid https://example.com", 2, "https://example.com")]
    // No extra args
    [InlineData("\"exe\" appid", 2, null)]
    [InlineData("exe", 1, null)]
    // Extra whitespace between tokens
    [InlineData("exe  appid   a  b", 2, "a  b")]
    // Quoted exe with spaces
    [InlineData("\"C:\\My App\\exe.exe\" appid --flag", 2, "--flag")]
    // Skip 0 — returns whole string (trimmed of leading whitespace)
    [InlineData("a b c", 0, "a b c")]
    [InlineData("  a b c", 0, "a b c")]
    // Skip 1
    [InlineData("\"exe\" a b \"c d\"", 1, "a b \"c d\"")]
    // Backslash before quote in token (not literal quote) — even backslashes → toggle
    [InlineData("\"C:\\\\\" appid rest", 2, "rest")]
    public void SliceVerbatimTail_ReturnsRemainderVerbatim(string cmdLine, int count, string? expected)
    {
        Assert.Equal(expected, CommandLineHelper.SliceVerbatimTail(cmdLine, count));
        Assert.Equal(expected, CommandLineHelper.SkipArgs(cmdLine, count));
    }

    // --- MaterializeProcessArguments / JoinArgs ---

    [Theory]
    [InlineData(new[] { "a", "b", "c d", "e" }, "a b \"c d\" e")]
    [InlineData(new[] { "https://example.com" }, "https://example.com")]
    [InlineData(new[] { "--flag", "C:\\My Path\\file.txt" }, "--flag \"C:\\My Path\\file.txt\"")]
    [InlineData(new[] { "no-space" }, "no-space")]
    [InlineData(new string[0], null)]
    [InlineData(null, null)]
    // Tab triggers quoting
    [InlineData(new[] { "arg\twith\ttabs" }, "\"arg\twith\ttabs\"")]
    // Embedded quotes are escaped
    [InlineData(new[] { "say \"hello\"" }, "\"say \\\"hello\\\"\"")]
    // Trailing backslash inside quoted arg (path with space forces quoting) is doubled
    [InlineData(new[] { @"C:\my path\" }, "\"C:\\my path\\\\\"")]
    [InlineData(new[] { @"C:\" }, @"C:\")]
    [InlineData(new[] { @"\\server\share\" }, @"\\server\share\")]
    public void JoinArgs_QuotesArgsWithSpaces(string[]? args, string? expected)
    {
        Assert.Equal(expected, CommandLineHelper.MaterializeProcessArguments(args));
        Assert.Equal(expected, CommandLineHelper.JoinArgs(args));
    }

    // --- ParseProcessArguments / SplitArgs ---

    [Theory]
    // Simple space-separated args
    [InlineData("a b c", new[] { "a", "b", "c" })]
    // Quoted arg with spaces
    [InlineData("\"hello world\" foo", new[] { "hello world", "foo" })]
    // Embedded escaped quote
    [InlineData("\"say \\\"hi\\\"\"", new[] { "say \"hi\"" })]
    // Standard browser command format: "<launcher>" "<appid>" "%1"
    [InlineData("\"C:\\launcher.exe\" \"myappid\" \"%1\"", new[] { "C:\\launcher.exe", "myappid", "%1" })]
    [InlineData("\"C:\\launcher.exe\"\t\"myappid\"\t\"%1\"", new[] { "C:\\launcher.exe", "myappid", "%1" })]
    [InlineData("\"arg with trailing slash\\\\\"", new[] { "arg with trailing slash\\" })]
    [InlineData("\"\\\\server\\share\\\\\"", new[] { "\\\\server\\share\\" })]
    // Empty string produces no args
    [InlineData("", new string[0])]
    // Whitespace only produces no args
    [InlineData("   ", new string[0])]
    public void ParseProcessArguments_ReturnsUnquotedValues(string cmdLine, string[] expected)
    {
        Assert.Equal(expected, CommandLineHelper.ParseProcessArguments(cmdLine));
        Assert.Equal(expected, CommandLineHelper.SplitArgs(cmdLine));
    }

    [Fact]
    public void ParseProcessCommandLine_UsesProgramNameSpecialRules_ForArgv0()
    {
        var parsed = CommandLineHelper.ParseProcessCommandLine(@"""C:\path with spaces\app.exe""arg0 --x");

        Assert.Equal("C:\\path with spaces\\app.exearg0", parsed[0]);
        Assert.Equal("--x", parsed[1]);
    }

    [Fact]
    public void QuoteProcessArgument_QuotesAndEscapesForSingleArgumentRoundTrip()
    {
        var raw = "tab\tspace slash\\ quote\" end\\";
        var quoted = CommandLineHelper.QuoteProcessArgument(raw);
        var parsed = CommandLineHelper.ParseProcessArguments(quoted);

        Assert.Single(parsed);
        Assert.Equal(raw, parsed[0]);
    }
}
