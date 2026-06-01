using Moq;
using RunFence.Account;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.SidMigration;
using RunFence.SidMigration.UI;
using RunFence.SidMigration.UI.Forms;
using RunFence.Tests.Helpers;
using RunFence.UI.Controls;
using RunFence.UI.Forms;
using Xunit;

namespace RunFence.Tests;

public class SidMigrationContextHelpTests
{
    [Fact]
    public void SidMigrationDialog_DiscoveryProgressStep_DoesNotRegisterContextHelpForMountedProgressControls()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var discoveryCompletion = new TaskCompletionSource<List<OrphanedSid>>();
            using var context = CreateDialogContext(
                discoverTask: discoveryCompletion.Task,
                scanTask: Task.FromResult(new List<SidMigrationMatch>()),
                applyTask: Task.FromResult((0L, 0L)));
            using var dialog = CreateDialog(context);
            StaTestHelper.CreateControlTree(dialog);

            var pathStep = FindControl<SidMigrationPathStep>(dialog);
            pathStep.RestoreState([(@"C:\test-root", true)]);

            StaTestHelper.ClickButton(FindButton(dialog, "Discover"));
            StaTestHelper.PumpUntil(
                () => FindControls<SidMigrationDiscoveryProgressStep>(dialog).Any(),
                timeoutMessage: "Timed out waiting for the discovery progress step to mount.");

            var progressStep = FindControl<SidMigrationDiscoveryProgressStep>(dialog);
            var descriptionLabel = GetDescriptionLabel(progressStep);

            Assert.False(dialog.TryGetContextHelp(progressStep, out _));
            Assert.False(dialog.TryGetContextHelp(descriptionLabel, out _));
            Assert.False(dialog.TryGetContextHelp(progressStep.ProgressBar, out _));
            Assert.False(dialog.TryGetContextHelp(progressStep.StatusLabel, out _));
            Assert.False(dialog.TryGetContextHelp(progressStep.CancelButton, out _));

            discoveryCompletion.SetResult([]);
            StaTestHelper.PumpUntil(
                () => !FindControls<SidMigrationDiscoveryProgressStep>(dialog).Any(),
                timeoutMessage: "Timed out waiting for the discovery step to complete.");
        });
    }

    [Fact]
    public void MigrationMappingStep_BeginAsync_DoesNotRegisterRedundantContextHelpForRuntimeBuiltContent()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var context = CreateDialogContext(
                discoverTask: Task.FromResult(new List<OrphanedSid>()),
                scanTask: Task.FromResult(new List<SidMigrationMatch>()),
                applyTask: Task.FromResult((0L, 0L)));
            using var host = new ContextHelpForm();
            using var step = new MigrationMappingStep(
                context.Session,
                context.SidMigrationService.Object,
                context.LocalUserProvider.Object,
                Mock.Of<ILoggingService>(),
                [],
                Mock.Of<IProfilePathResolver>(),
                Mock.Of<ISidNameCacheService>(),
                Mock.Of<IMessageBoxService>());
            host.Controls.Add(step);
            StaTestHelper.CreateControlTree(host);

            var ready = false;
            step.BeginAsync(() => ready = true, () => Assert.Fail("Mapping build should not fail."));
            StaTestHelper.PumpUntil(() => ready, timeoutMessage: "Timed out waiting for mapping content.");

            var mappingRoot = step.Controls.OfType<Panel>().Single();
            var mappingGrid = FindControl<StyledDataGridView>(mappingRoot);
            var detailsList = FindControl<ListBox>(mappingRoot);
            var toolStrip = FindControl<ToolStrip>(mappingRoot);

            Assert.False(host.TryGetContextHelp(mappingRoot, out _));
            Assert.Null(TryResolveHelpText(host, mappingGrid));
            Assert.Null(TryResolveHelpText(host, detailsList));
            Assert.Null(TryResolveToolStripHelpText(host, toolStrip));
        });
    }

    private sealed record DialogContext(
        SessionContext Session,
        Mock<ISidMigrationService> SidMigrationService,
        Mock<ILocalUserProvider> LocalUserProvider) : IDisposable
    {
        public void Dispose() => Session.Dispose();
    }

    private static DialogContext CreateDialogContext(
        Task<List<OrphanedSid>> discoverTask,
        Task<List<SidMigrationMatch>> scanTask,
        Task<(long applied, long errors)> applyTask)
    {
        var session = new SessionContext
{
            Database = new AppDatabase { SidNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) },
            CredentialStore = new CredentialStore
            {
                Credentials =
                [
                    new CredentialEntry { Sid = "S-1-5-21-old" }
                ]
            },
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));
        session.Database.SidNames["S-1-5-21-old"] = "old-user";
        var sidMigrationService = new Mock<ISidMigrationService>();
        sidMigrationService.Setup(s => s.BuildMappings(
                It.IsAny<IReadOnlyList<CredentialEntry>>(),
                It.IsAny<IReadOnlyList<LocalUserAccount>>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>()))
            .Returns([
                new SidMigrationMapping("S-1-5-21-old", "old-user", "S-1-5-21-new")
            ]);
        sidMigrationService.Setup(s => s.DiscoverOrphanedSidsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IProgress<(long scanned, long sidsFound)>>(),
                It.IsAny<CancellationToken>()))
            .Returns(discoverTask);
        sidMigrationService.Setup(s => s.ScanAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyList<SidMigrationMapping>>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IProgress<(long scanned, long found)>>(),
                It.IsAny<CancellationToken>()))
            .Returns(scanTask);
        sidMigrationService.Setup(s => s.ApplyAsync(
                It.IsAny<IReadOnlyList<SidMigrationMatch>>(),
                It.IsAny<IReadOnlyList<SidMigrationMapping>>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IProgress<MigrationProgress>>(),
                It.IsAny<CancellationToken>()))
            .Returns(applyTask);

        var localUserProvider = new Mock<ILocalUserProvider>();
        localUserProvider.Setup(s => s.GetLocalUserAccounts()).Returns([
            new LocalUserAccount("new-user", "S-1-5-21-new")
        ]);

        return new DialogContext(session, sidMigrationService, localUserProvider);
    }

    private static string? TryResolveHelpText(ContextHelpForm host, Control control)
    {
        var point = control.PointToScreen(new Point(Math.Min(1, Math.Max(0, control.Width - 1)), Math.Min(1, Math.Max(0, control.Height - 1))));
        var target = ContextHelpTextResolver.Resolve(host, new ContextHelpButton(), control, point);
        return target?.HelpText;
    }

    private static string? TryResolveToolStripHelpText(ContextHelpForm host, ToolStrip toolStrip)
    {
        var point = toolStrip.PointToScreen(new Point(4, 4));
        var target = ContextHelpTextResolver.Resolve(host, new ContextHelpButton(), toolStrip, point);
        return target?.HelpText;
    }

    private static SidMigrationDialog CreateDialog(DialogContext context)
        => new(
            new SidMigrationWorkflowController(
                context.Session,
                context.SidMigrationService.Object,
                Mock.Of<IMessageBoxService>(),
                new SidMigrationWorkflowState(),
                new SidMigrationProgressCoordinator(
                    Mock.Of<ILoggingService>(),
                    Mock.Of<IMessageBoxService>(),
                    new SidMigrationDiskApplyController(Mock.Of<IClock>())),
                new SidMigrationStepFactory(
                    context.Session,
                    context.SidMigrationService.Object,
                    null!,
                    context.LocalUserProvider.Object,
                    Mock.Of<ILoggingService>(),
                    Mock.Of<IProfilePathResolver>(),
                    Mock.Of<ISidNameCacheService>(),
                    Mock.Of<IMessageBoxService>()),
                new SidMigrationDiscoveryStepController(),
                new SidMigrationDiskScanStepController()));

    private static Button FindButton(Control root, string textPrefix)
        => FindControls<Button>(root).First(control => control.Text.StartsWith(textPrefix, StringComparison.Ordinal));

    private static T FindControl<T>(Control root, bool optional = false) where T : Control
    {
        var controls = FindControls<T>(root).ToList();
        if (optional)
            return controls.SingleOrDefault()!;

        return controls.Single();
    }

    private static IEnumerable<T> FindControls<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
                yield return match;

            foreach (var nested in FindControls<T>(child))
                yield return nested;
        }
    }

    private static Label GetDescriptionLabel(MigrationProgressStep step)
        => step.Controls.OfType<Label>().Single(control => control.Dock == DockStyle.Top);
}
