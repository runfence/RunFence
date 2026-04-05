using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public class CommandLineHelperTests
{
    // --- SkipArgs ---

    [Theory]
    // Preserves exact original quoting — the core invariant
    [InlineData("\"exe\" appid a b \"c d\" \"e\"", 2, "a b \"c d\" \"e\"")]
    [InlineData("exe appid a b \"c d\" \"e\"", 2, "a b \"c d\" \"e\"")]
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
    public void SkipArgs_ReturnsRemainderVerbatim(string cmdLine, int count, string? expected)
    {
        Assert.Equal(expected, CommandLineHelper.SkipArgs(cmdLine, count));
    }

    // --- JoinArgs ---

    [Theory]
    [InlineData(new[] { "a", "b", "c d", "e" }, "a b \"c d\" e")]
    [InlineData(new[] { "https://example.com" }, "https://example.com")]
    [InlineData(new[] { "--flag", "C:\\My Path\\file.txt" }, "--flag \"C:\\My Path\\file.txt\"")]
    [InlineData(new[] { "no-space" }, "no-space")]
    [InlineData(new string[0], null)]
    // Tab triggers quoting
    [InlineData(new[] { "arg\twith\ttabs" }, "\"arg\twith\ttabs\"")]
    // Embedded quotes are escaped
    [InlineData(new[] { "say \"hello\"" }, "\"say \\\"hello\\\"\"")]
    // Trailing backslash inside quoted arg (path with space forces quoting) is doubled
    [InlineData(new[] { @"C:\my path\" }, "\"C:\\my path\\\\\"")]
    public void JoinArgs_QuotesArgsWithSpaces(string[] args, string? expected)
    {
        Assert.Equal(expected, CommandLineHelper.JoinArgs(args));
    }

    [Fact]
    public void JoinArgs_NullInput_ReturnsNull()
    {
        Assert.Null(CommandLineHelper.JoinArgs(null));
    }

    // --- SplitArgs ---

    [Theory]
    // Simple space-separated args
    [InlineData("a b c", new[] { "a", "b", "c" })]
    // Quoted arg with spaces
    [InlineData("\"hello world\" foo", new[] { "hello world", "foo" })]
    // Embedded escaped quote
    [InlineData("\"say \\\"hi\\\"\"", new[] { "say \"hi\"" })]
    // Standard browser command format: "<launcher>" "<appid>" "%1"
    [InlineData("\"C:\\launcher.exe\" \"myappid\" \"%1\"", new[] { "C:\\launcher.exe", "myappid", "%1" })]
    // Empty string produces no args
    [InlineData("", new string[0])]
    // Whitespace only produces no args
    [InlineData("   ", new string[0])]
    public void SplitArgs_ReturnsUnquotedValues(string cmdLine, string[] expected)
    {
        Assert.Equal(expected, CommandLineHelper.SplitArgs(cmdLine));
    }
}