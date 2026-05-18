using System.Threading;
using RunFence.Account.OrphanedProfiles;
using RunFence.Account.UI.OrphanedProfiles;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class OrphanedProfilesSelectionPanelTests
{
    [Fact]
    public void Populate_StartsSizeCalculationAndKeepsCheckedStateStable()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var service = new ControlledOrphanedProfileService();
            var profile = new OrphanedProfile(null, @"C:\Users\Ghost");

            using var panel = new OrphanedProfilesSelectionPanel(service);
            StaTestHelper.CreateControlTree(panel);
            panel.Populate([profile]);

            StaTestHelper.PumpUntil(
                () => service.Started.IsSet && GetProfileListView(panel).Items.Count == 1 && GetProfileListView(panel).Items[0].SubItems[1].Text == "0+",
                timeoutMessage: "Timed out waiting for size calculation to start.");

            var item = GetProfileListView(panel).Items[0];
            Assert.True(item.Checked);
            Assert.False(item.Selected);

            service.ReleaseCalculation.Set();

            StaTestHelper.PumpUntil(
                () => item.SubItems[1].Text == "15",
                timeoutMessage: "Timed out waiting for the final size value.");

            Assert.True(item.Checked);
            Assert.False(item.Selected);
        });
    }

    [Fact]
    public void StopSizeCalculation_CancelsActiveCalculation()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var service = new ControlledOrphanedProfileService();
            using var panel = new OrphanedProfilesSelectionPanel(service);
            StaTestHelper.CreateControlTree(panel);
            panel.Populate([new OrphanedProfile(null, @"C:\Users\Ghost")]);

            StaTestHelper.PumpUntil(
                () => service.Started.IsSet,
                timeoutMessage: "Timed out waiting for size calculation to start.");

            panel.StopSizeCalculation();

            StaTestHelper.PumpUntil(
                () => service.Canceled.IsSet,
                timeoutMessage: "Timed out waiting for size calculation cancellation.");
        });
    }

    private static ListView GetProfileListView(Control root) =>
        EnumerateControls(root).OfType<ListView>().Single();

    private static IEnumerable<Control> EnumerateControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in EnumerateControls(child))
                yield return nested;
        }
    }

    private sealed class ControlledOrphanedProfileService : IOrphanedProfileService
    {
        public ManualResetEventSlim Started { get; } = new(false);

        public ManualResetEventSlim ReleaseCalculation { get; } = new(false);

        public ManualResetEventSlim Canceled { get; } = new(false);

        public List<OrphanedProfile> GetOrphanedProfiles() => throw new NotSupportedException();

        public long GetProfileSizeBytes(string profilePath, IProgress<long>? progress, CancellationToken cancellationToken)
        {
            progress?.Report(0);
            Started.Set();

            try
            {
                ReleaseCalculation.Wait(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Canceled.Set();
                throw;
            }

            progress?.Report(15);
            return 15L * 1024L * 1024L;
        }

        public (List<string> Deleted, List<(string Path, string Error)> Failed) DeleteProfiles(IEnumerable<OrphanedProfile> profiles)
            => throw new NotSupportedException();

        public void CleanupLogonScripts(string sid) => throw new NotSupportedException();
    }
}
