using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.SidMigration.UI;
using Xunit;

namespace RunFence.Tests;

public class SidMigrationControllerTests
{
    private const string Unresolved1 = "S-1-5-21-1000000001-1000000002-1000000003-1001";
    private const string Unresolved2 = "S-1-5-21-1000000001-1000000002-1000000003-1002";
    private const string Confirmed = "S-1-5-21-1000000001-1000000002-1000000003-1003";

    [Fact]
    public void BuildDiscoveryResult_WithUnresolvedSids_ProvidesWarningAndCounts()
    {
        var controller = new SidMigrationDiscoveryStepController();
        var orphanedSids = new List<OrphanedSid>
        {
            new() { Sid = Confirmed, Classification = OrphanedSidClassification.ConfirmedOrphaned },
            new() { Sid = Unresolved1, Classification = OrphanedSidClassification.Unresolved },
            new() { Sid = Unresolved2, Classification = OrphanedSidClassification.Unresolved }
        };

        var result = controller.BuildResult(orphanedSids);

        Assert.Equal($"Discovery finished.{Environment.NewLine}{Environment.NewLine}" +
            "Confirmed orphaned SIDs: 1" + Environment.NewLine +
            "Unresolved lookups: 2",
            result.CompletionText);
        Assert.Equal("Scan cancelled. Confirmed: 1, unresolved: 2.", result.CancelText);
        Assert.NotNull(result.UnresolvedWarningMessage);
        Assert.Contains(Unresolved1, result.UnresolvedWarningMessage);
        Assert.Contains(Unresolved2, result.UnresolvedWarningMessage);
        Assert.Contains("Unresolved SIDs stay skipped unless you explicitly include them in the next step.", result.UnresolvedWarningMessage);
    }

    [Fact]
    public void BuildDiscoveryResult_WithoutUnresolvedSids_HasNoWarningMessage()
    {
        var controller = new SidMigrationDiscoveryStepController();
        var orphanedSids = new List<OrphanedSid>
        {
            new() { Sid = Confirmed, Classification = OrphanedSidClassification.ConfirmedOrphaned }
        };

        var result = controller.BuildResult(orphanedSids);

        Assert.Equal($"Discovery finished.{Environment.NewLine}{Environment.NewLine}" +
            "Confirmed orphaned SIDs: 1" + Environment.NewLine +
            "Unresolved lookups: 0",
            result.CompletionText);
        Assert.Equal("Scan cancelled. Confirmed: 1, unresolved: 0.", result.CancelText);
        Assert.Null(result.UnresolvedWarningMessage);
    }

    [Fact]
    public void BuildDiskScanResult_FindsOwnerDeleteBlockingSidsCaseInsensitiveAndDistinct()
    {
        var controller = new SidMigrationDiskScanStepController();
        var scanResults = new List<SidMigrationMatch>
        {
            new() { MatchType = SidMigrationMatchType.Owner, OwnerOldSid = "S-1-5-21-1111" },
            new() { MatchType = SidMigrationMatchType.Owner | SidMigrationMatchType.Ace, OwnerOldSid = "s-1-5-21-1111" },
            new() { MatchType = SidMigrationMatchType.Ace, OwnerOldSid = "S-1-5-21-2222" },
            new() { MatchType = SidMigrationMatchType.Owner, OwnerOldSid = "S-1-5-21-3333" }
        };

        var result = controller.BuildResult(scanResults, ["S-1-5-21-1111", "s-1-5-21-3333"]);

        Assert.Equal(2, result.OwnerDeleteBlockingSids.Count);
        var ownerDeleteBlockingSids = result.OwnerDeleteBlockingSids.ToList();
        Assert.Contains("S-1-5-21-1111", ownerDeleteBlockingSids);
        Assert.Contains("S-1-5-21-3333", ownerDeleteBlockingSids);

        Assert.Equal("Scan cancelled. Found 4 items so far.", result.CancelText);
    }

    [Fact]
    public void BuildDiskScanResult_WithNoResults_ReportsEmptyCancelText()
    {
        var controller = new SidMigrationDiskScanStepController();

        var result = controller.BuildResult([], []);

        Assert.Empty(result.OwnerDeleteBlockingSids);
        Assert.Equal("Scan cancelled.", result.CancelText);
    }

    [Fact]
    public void TryRequestCancellation_BeforeThreshold_DoesNotPromptOrCancel()
    {
        var clock = new ManualClock(DateTime.UtcNow);
        var controller = new SidMigrationDiskApplyController(clock);
        controller.Start();
        clock.UtcNow = clock.UtcNow.AddSeconds(5);

        var confirmationRequested = false;
        using var cts = new CancellationTokenSource();

        var canceled = controller.TryRequestCancellation(cts, () =>
        {
            confirmationRequested = true;
            return true;
        });

        Assert.False(canceled);
        Assert.False(confirmationRequested);
        Assert.False(cts.IsCancellationRequested);
    }

    [Fact]
    public void TryRequestCancellation_AfterThreshold_ConfirmsAndCancels()
    {
        var clock = new ManualClock(DateTime.UtcNow);
        var controller = new SidMigrationDiskApplyController(clock);
        controller.Start();
        clock.UtcNow = clock.UtcNow.AddSeconds(11);

        var confirmationRequests = 0;
        using var cts = new CancellationTokenSource();

        var canceled = controller.TryRequestCancellation(cts, () =>
        {
            confirmationRequests++;
            return true;
        });

        Assert.True(canceled);
        Assert.Equal(1, confirmationRequests);
        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void TryRequestCancellation_AfterThreshold_WhenNotConfirmed_DoesNotCancel()
    {
        var clock = new ManualClock(DateTime.UtcNow);
        var controller = new SidMigrationDiskApplyController(clock);
        controller.Start();
        clock.UtcNow = clock.UtcNow.AddSeconds(11);

        var confirmationRequests = 0;
        using var cts = new CancellationTokenSource();

        var canceled = controller.TryRequestCancellation(cts, () =>
        {
            confirmationRequests++;
            return false;
        });

        Assert.False(canceled);
        Assert.Equal(1, confirmationRequests);
        Assert.False(cts.IsCancellationRequested);
    }

    private sealed class ManualClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
    }
}
