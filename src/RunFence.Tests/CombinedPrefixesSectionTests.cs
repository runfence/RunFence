using RunFence.Apps.UI.Forms;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="CombinedPrefixesSection"/> prefix UI behavior.
/// All tests run on an STA thread because WinForms controls require it.
/// </summary>
public class CombinedPrefixesSectionTests
{
    [Fact]
    public void FlatMode_CollectsOnlyAssociationPrefixes()
    {
        StaTestHelper.RunOnSta(() =>
        {
            // Arrange: enable flat mode before any data is loaded
            using var section = new CombinedPrefixesSection();
            section.EnableFlatMode();

            var assocPrefixes = new[] { @"https://work.", @"https://internal." };

            // Act
            section.SetAssociationPrefixes(assocPrefixes, replacePrefixes: false);
            var assocResult = section.GetAssociationPrefixes();
            var appResult = section.GetAppPrefixes();

            // Assert: flat mode returns association prefixes; app section is suppressed
            Assert.NotNull(assocResult);
            Assert.Equal(assocPrefixes, assocResult);
            Assert.Null(appResult);
        });
    }

    [Fact]
    public void FlatMode_SetAppPrefixes_IsNoOp()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var section = new CombinedPrefixesSection();
            section.EnableFlatMode();

            // SetAppPrefixes is a no-op in flat mode
            section.SetAppPrefixes(new[] { @"C:\Apps\" });

            // App section returns null (no-op)
            Assert.Null(section.GetAppPrefixes());
        });
    }

    [Fact]
    public void SwitchingAddToReplace_DoesNotThrow()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var section = new CombinedPrefixesSection();

            // Arrange: load some app-level and assoc-level prefixes in Add mode
            section.SetAppPrefixes(new[] { @"C:\Apps\" });
            section.SetAssociationPrefixes(new[] { @"C:\Work\" }, replacePrefixes: false);
            Assert.False(section.IsReplace);

            // Act: switch to Replace mode — which hides the app section.
            // The selection fix should prevent any exception from DataGridView.
            var exception = Record.Exception(() =>
                section.SetAssociationPrefixes(new[] { @"C:\Work\" }, replacePrefixes: true));

            // Assert
            Assert.Null(exception);
            Assert.True(section.IsReplace);
        });
    }

    [Fact]
    public void SwitchAddToReplace_AppPrefixesPreserved_AssocPrefixesStillReadable()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var section = new CombinedPrefixesSection();

            section.SetAppPrefixes(new[] { @"C:\Apps\" });
            section.SetAssociationPrefixes(new[] { @"C:\Work\" }, replacePrefixes: false);

            // Switch to Replace: app section hidden but data preserved
            section.SetAssociationPrefixes(new[] { @"C:\Work\" }, replacePrefixes: true);

            // Association prefixes should still be readable
            var assocResult = section.GetAssociationPrefixes();
            Assert.NotNull(assocResult);
            Assert.Single(assocResult);
            Assert.Equal(@"C:\Work\", assocResult[0]);

            // Switch back to Add: app prefixes should be restored and readable
            section.SetAssociationPrefixes(new[] { @"C:\Work\" }, replacePrefixes: false);
            var appResult = section.GetAppPrefixes();
            Assert.NotNull(appResult);
            Assert.Single(appResult);
            Assert.Equal(@"C:\Apps\", appResult[0]);
        });
    }

    [Fact]
    public void GetAssociationPrefixes_WhitespaceOnlyEntries_ReturnsNull()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var section = new CombinedPrefixesSection();

            // Prefixes with only whitespace should be filtered out by normalization
            section.SetAssociationPrefixes(new[] { "   " }, replacePrefixes: false);
            var result = section.GetAssociationPrefixes();

            Assert.Null(result);
        });
    }
}
