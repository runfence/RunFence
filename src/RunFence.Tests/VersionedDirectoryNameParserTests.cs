using RunFence.Launching.Resolution;
using Xunit;

namespace RunFence.Tests;

public sealed class VersionedDirectoryNameParserTests
{
    [Theory]
    [InlineData("app-4.50.121", "app-", "4.50.121", "")]
    [InlineData("Claude_1.1.5749.0_x64__pzs8sxrjxfjjc", "Claude_", "1.1.5749.0", "_x64__pzs8sxrjxfjjc")]
    [InlineData("JetBrains Rider 2026.1b", "JetBrains Rider ", "2026.1b", "")]
    [InlineData("slack-v4.50.121_skfsdlfkdsmflk", "slack-v", "4.50.121", "_skfsdlfkdsmflk")]
    public void TryParse_ExampleNames_ParsesVersionAndIdentityParts(
        string folderName,
        string expectedPrefix,
        string expectedVersion,
        string expectedSuffix)
    {
        var parsed = AssertParse(folderName);

        Assert.Equal(expectedPrefix, parsed.Prefix);
        Assert.Equal(expectedVersion, parsed.Version);
        Assert.Equal(expectedSuffix, parsed.Suffix);
        Assert.Equal(folderName, parsed.OriginalName);
    }

    [Fact]
    public void SemanticVersionKey_CompareTo_UsesNumericOrderingInsteadOfLexicographicOrdering()
    {
        var lower = AssertParse("Vendor 11.9").SemanticVersionKey;
        var higher = AssertParse("Vendor 11.10").SemanticVersionKey;

        Assert.True(higher.CompareTo(lower) > 0);
    }

    [Fact]
    public void SemanticVersionKey_CompareTo_TreatsReleaseAsHigherThanSuffixVariant()
    {
        var preview = AssertParse("Rider 2026.1b").SemanticVersionKey;
        var release = AssertParse("Rider 2026.1").SemanticVersionKey;

        Assert.True(release.CompareTo(preview) > 0);
    }

    [Fact]
    public void TryParse_MultipleVersionTokens_ReturnsFalse()
    {
        Assert.False(VersionedDirectoryNameParser.TryParse("App 1.0 build 2.0", out _));
    }

    [Fact]
    public void TryParse_NoSemanticVersionToken_ReturnsFalse()
    {
        Assert.False(VersionedDirectoryNameParser.TryParse("Program Files (x86)", out _));
    }

    private static VersionedDirectoryNameParser.VersionedDirectoryName AssertParse(string folderName)
    {
        Assert.True(VersionedDirectoryNameParser.TryParse(folderName, out var parsed));
        return parsed;
    }
}
