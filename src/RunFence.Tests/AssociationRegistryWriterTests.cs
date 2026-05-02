using RunFence.Apps;
using Xunit;

namespace RunFence.Tests;

public class AssociationRegistryWriterTests
{
    // --- F-81: IsTargetSidSubKey static string-matching ---

    [Theory]
    [InlineData("S-1-5-21-1234567890-1234567890-1234567890-1001")]
    [InlineData("S-1-5-21-111-222-333-500")]
    [InlineData("S-1-5-21-100-200-300-1000")]
    [InlineData("s-1-5-21-100-200-300-1001")] // lowercase prefix (OrdinalIgnoreCase)
    public void IsTargetSidSubKey_UserSid_ReturnsTrue(string name)
    {
        Assert.True(AssociationRegistryWriter.IsTargetSidSubKey(name));
    }

    [Theory]
    [InlineData("S-1-5-21-1234567890-1234567890-1234567890-1001_Classes")] // _Classes suffix excluded
    [InlineData("S-1-5-21-100-200-300-1001_classes")]                     // _classes — OrdinalIgnoreCase
    [InlineData("S-1-5-21-100-200-300-1001_CLASSES")]                     // _CLASSES — OrdinalIgnoreCase
    [InlineData("S-1-5-18")]           // local system — not S-1-5-21-*
    [InlineData("S-1-5-32-544")]       // Administrators — not S-1-5-21-*
    [InlineData(".Default")]
    [InlineData("LocalService")]
    [InlineData("S-1-5-20")]           // NetworkService — not S-1-5-21-*
    public void IsTargetSidSubKey_NonUserSidOrClassesVariant_ReturnsFalse(string name)
    {
        Assert.False(AssociationRegistryWriter.IsTargetSidSubKey(name));
    }
}
