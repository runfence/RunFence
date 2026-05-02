using RunFence.SidMigration.UI;
using Xunit;

namespace RunFence.Tests;

public class SidMigrationMappingValidatorTests
{
    private const string Sid = "S-1-5-21-1111111111-1111111111-1111111111-1001";

    [Theory]
    [InlineData(Sid)]
    [InlineData("Vlad (S-1-5-21-1111111111-1111111111-1111111111-1001)")]
    [InlineData("Vlad Local (S-1-5-21-1111111111-1111111111-1111111111-1001)")]
    public void TryResolveSidInput_ValidRawOrDisplayedSid_ReturnsCanonicalSid(string input)
    {
        var displayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Sid] = $"Vlad ({Sid})"
        };

        var result = SidMigrationMappingValidator.TryResolveSidInput(input, displayNames, out var canonicalSid);

        Assert.True(result);
        Assert.Equal(Sid, canonicalSid);
    }

    [Fact]
    public void TryResolveSidInput_InvalidInput_ReturnsFalse()
    {
        var result = SidMigrationMappingValidator.TryResolveSidInput(
            "Vlad",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            out _);

        Assert.False(result);
    }
}
