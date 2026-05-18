using Moq;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.SidMigration;
using RunFence.SidMigration.UI;
using Xunit;

namespace RunFence.Tests;

public class SidMigrationSelectionCollectorTests
{
    private const string OldSidToMigrate = "S-1-5-21-1000000001-1000000002-1000000003-1001";
    private const string OldSidToDelete = "S-1-5-21-1000000001-1000000002-1000000003-1002";
    private const string NewSid = "S-1-5-21-2000000001-2000000002-2000000003-1001";

    [Fact]
    public void TryCollect_MixedMigrateAndDelete_ReturnsBothActionSets()
    {
        var messageBoxService = new Mock<IMessageBoxService>(MockBehavior.Strict);
        var collector = CreateCollector(messageBoxService.Object);

        var result = collector.Collect(
            [
                new SidMigrationSelectionRow(0, "Migrate", OldSidToMigrate, NewSid, "Migrated User"),
                new SidMigrationSelectionRow(1, "Delete", OldSidToDelete, "", "Orphaned User")
            ],
            _ => true);

        Assert.True(result.Success);
        var mapping = Assert.Single(result.Mappings);
        Assert.Equal(OldSidToMigrate, mapping.OldSid);
        Assert.Equal(NewSid, mapping.NewSid);
        Assert.Equal([OldSidToDelete], result.DeleteSids);
        messageBoxService.VerifyNoOtherCalls();
    }

    [Fact]
    public void TryCollect_DeleteBlockedByOwnerReferences_FailsInsteadOfDroppingDelete()
    {
        var messageBoxService = new Mock<IMessageBoxService>();
        messageBoxService
            .Setup(s => s.Show(
                It.Is<string>(text => text.Contains(OldSidToDelete, StringComparison.Ordinal)),
                "Owner Migration Required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning))
            .Returns(DialogResult.OK);
        var collector = CreateCollector(
            messageBoxService.Object,
            orphanedBySid: new Dictionary<string, OrphanedSid>(StringComparer.OrdinalIgnoreCase)
            {
                [OldSidToDelete] = new()
                {
                    Sid = OldSidToDelete,
                    Classification = OrphanedSidClassification.ConfirmedOrphaned,
                    OwnerCount = 1
                }
            });

        var result = collector.Collect(
            [new SidMigrationSelectionRow(0, "Delete", OldSidToDelete, "", "Orphaned User")],
            _ => true);

        Assert.False(result.Success);
        Assert.Empty(result.Mappings);
        Assert.Empty(result.DeleteSids);
        messageBoxService.VerifyAll();
    }

    [Fact]
    public void TryCollect_UnresolvedMigrateAndDelete_PromptsOnceForCombinedSelection()
    {
        var messageBoxService = new Mock<IMessageBoxService>(MockBehavior.Strict);
        var collector = CreateCollector(
            messageBoxService.Object,
            orphanedBySid: new Dictionary<string, OrphanedSid>(StringComparer.OrdinalIgnoreCase)
            {
                [OldSidToMigrate] = new()
                {
                    Sid = OldSidToMigrate,
                    Classification = OrphanedSidClassification.Unresolved
                },
                [OldSidToDelete] = new()
                {
                    Sid = OldSidToDelete,
                    Classification = OrphanedSidClassification.Unresolved
                }
            });

        var confirmCalls = 0;
        var result = collector.Collect(
            [
                new SidMigrationSelectionRow(0, "Migrate", OldSidToMigrate, NewSid, "Migrated User"),
                new SidMigrationSelectionRow(1, "Delete", OldSidToDelete, "", "Orphaned User")
            ],
            selectedSids =>
            {
                confirmCalls++;
                Assert.Equal(
                    [OldSidToMigrate, OldSidToDelete],
                    selectedSids.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray());
                return true;
            });

        Assert.True(result.Success);
        Assert.Single(result.Mappings);
        Assert.Equal([OldSidToDelete], result.DeleteSids);
        Assert.Equal(1, confirmCalls);
        messageBoxService.VerifyNoOtherCalls();
    }

    private static SidMigrationSelectionCollector CreateCollector(
        IMessageBoxService messageBoxService,
        IReadOnlyDictionary<string, OrphanedSid>? orphanedBySid = null)
    {
        var validator = new SidMigrationMappingValidator(Mock.Of<IProfilePathResolver>());
        return new SidMigrationSelectionCollector(
            validator,
            messageBoxService,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [NewSid] = $"Replacement User ({NewSid})"
            },
            orphanedBySid ?? new Dictionary<string, OrphanedSid>(StringComparer.OrdinalIgnoreCase));
    }
}
