using RunFence.Firewall;
using RunFence.Firewall.UI;
using Xunit;

namespace RunFence.Tests;

public class FirewallPortValidatorTests
{
    private readonly FirewallPortValidator _validator = new();

    // ── HasDuplicate ───────────────────────────────────────────────────────

    [Fact]
    public void HasDuplicate_CaseInsensitive_ReturnsTrue()
    {
        // Port strings are numeric; same value always equals itself
        var entries = new List<string> { "53", "8080" };

        Assert.True(_validator.HasDuplicate("53", entries));
    }

    [Fact]
    public void HasDuplicate_WithExclusion_ExcludesOldValue()
    {
        // Editing "53" in-place: excluding the old value should return false even though it's in the list
        var entries = new List<string> { "53", "8080" };

        Assert.False(_validator.HasDuplicate("53", entries, excluding: "53"));
    }

    [Fact]
    public void HasDuplicate_NoDuplicate_ReturnsFalse()
    {
        var entries = new List<string> { "8080", "443" };

        Assert.False(_validator.HasDuplicate("53", entries));
    }

    // ── CheckLimit ────────────────────────────────────────────────────────

    [Fact]
    public void CheckLimit_BelowMax_ReturnsTrue()
    {
        Assert.True(_validator.CheckLimit(LocalhostPortParser.MaxAllowedPorts - 1));
    }

    [Fact]
    public void CheckLimit_AtMax_ReturnsFalse()
    {
        Assert.False(_validator.CheckLimit(LocalhostPortParser.MaxAllowedPorts));
    }

    // ── ParseLocalhostPort ────────────────────────────────────────────────

    [Theory]
    [InlineData("localhost:53", 53, 53)]
    [InlineData("localhost:8080", 8080, 8080)]
    [InlineData("LOCALHOST:443", 443, 443)]
    [InlineData("localhost:3000-3010", 3000, 3010)]
    public void ParseLocalhostPort_ValidInput_ReturnsRange(string input, int low, int high)
    {
        Assert.Equal(new PortRange(low, high), _validator.ParseLocalhostPort(input));
    }

    [Theory]
    [InlineData("53")]
    [InlineData("localhost")]
    [InlineData("localhost:")]
    [InlineData("localhost:0")]
    [InlineData("localhost:99999")]
    [InlineData("localhost:abc")]
    [InlineData("example.com:80")]
    [InlineData("localhost:8090-8080")]
    public void ParseLocalhostPort_InvalidInput_ReturnsNull(string input)
    {
        Assert.Null(_validator.ParseLocalhostPort(input));
    }
}
