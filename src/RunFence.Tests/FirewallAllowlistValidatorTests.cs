using Moq;
using RunFence.Firewall.UI;
using RunFence.Licensing;
using Xunit;

namespace RunFence.Tests;

public class FirewallAllowlistValidatorTests
{
    private readonly Mock<ILicenseService> _licenseService = new();
    private readonly FirewallAllowlistValidator _validator;

    public FirewallAllowlistValidatorTests()
    {
        _validator = new FirewallAllowlistValidator(_licenseService.Object);
    }

    // --- IsValidDomain: label length boundary (F-76) ---

    [Fact]
    public void IsValidDomain_LabelExceeds63Chars_ReturnsFalse()
    {
        var longLabel = new string('a', 64);
        Assert.False(_validator.IsValidDomain($"{longLabel}.example.com"));
    }

    [Fact]
    public void IsValidDomain_LabelExactly63Chars_ReturnsTrue()
    {
        var label63 = new string('a', 63);
        Assert.True(_validator.IsValidDomain($"{label63}.com"));
    }

    // --- IsValidDomain: total length boundary (F-76) ---

    [Fact]
    public void IsValidDomain_TotalLengthExceeds253_ReturnsFalse()
    {
        // 5 labels of 50 chars each = 250 + 4 dots = 254
        var label50 = new string('a', 50);
        var domain = string.Join(".", Enumerable.Repeat(label50, 5));
        Assert.True(domain.Length > 253);
        Assert.False(_validator.IsValidDomain(domain));
    }

    [Fact]
    public void IsValidDomain_TotalLengthExactly253_ReturnsTrue()
    {
        // 63 + 1 + 63 + 1 + 63 + 1 + 61 = 253
        var label63 = new string('a', 63);
        var domain = $"{label63}.{label63}.{label63}.{new string('b', 61)}";
        Assert.Equal(253, domain.Length);
        Assert.True(_validator.IsValidDomain(domain));
    }

    // --- IsValidDomain: valid domains (F-76) ---

    [Theory]
    [InlineData("example.com")]
    [InlineData("sub.example.com")]
    [InlineData("a.b.c.d")]
    [InlineData("example123.com")]
    [InlineData("my-host.example.com")]  // internal hyphen
    [InlineData("intranet")]             // single-label (corporate intranet)
    [InlineData("my-server")]            // single-label with internal hyphen
    public void IsValidDomain_ValidDomains_ReturnsTrue(string domain)
    {
        Assert.True(_validator.IsValidDomain(domain));
    }

    // --- IsValidDomain: invalid domains (F-76) ---

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("example..com")]   // empty label
    [InlineData("example.com!")]   // invalid char
    [InlineData(".example.com")]   // empty first label
    [InlineData("-example.com")]   // label starts with hyphen
    [InlineData("example-.com")]   // label ends with hyphen
    [InlineData("sub-.example.com")] // sub-label ends with hyphen
    [InlineData("-server")]        // single-label starts with hyphen
    public void IsValidDomain_InvalidDomains_ReturnsFalse(string domain)
    {
        Assert.False(_validator.IsValidDomain(domain));
    }
}
