using Moq;
using RunFence.Core.Models;
using RunFence.Firewall.UI;
using RunFence.Firewall.UI.Forms;
using RunFence.Launch;
using RunFence.Persistence;
using RunFence.Wizard;
using RunFence.Wizard.Templates;
using Xunit;

namespace RunFence.Tests;

public class AiAgentFirewallOrchestratorTests
{
    [Fact]
    public void BuildPostWizardAction_InternetNotRestrictedInWizard_ReturnsAction()
    {
        var orchestrator = CreateOrchestrator();

        var action = orchestrator.BuildPostWizardAction(
            sid: "S-1-5-21-100-200-300-1001",
            username: "Agent",
            internetRestrictedInWizard: false,
            session: new SessionContext { Database = new AppDatabase() },
            sessionSaver: Mock.Of<IWizardSessionSaver>(),
            toolPath: null);

        Assert.NotNull(action);
    }

    [Fact]
    public void BuildPostWizardAction_InternetRestrictedInWizard_ReturnsAction()
    {
        var orchestrator = CreateOrchestrator();

        var action = orchestrator.BuildPostWizardAction(
            sid: "S-1-5-21-100-200-300-1001",
            username: "Agent",
            internetRestrictedInWizard: true,
            session: new SessionContext { Database = new AppDatabase() },
            sessionSaver: Mock.Of<IWizardSessionSaver>(),
            toolPath: null);

        Assert.NotNull(action);
    }

    [Fact]
    public void PostWizardAction_InternetNotRestrictedInWizard_LaunchesToolWithoutOpeningFirewallUi()
    {
        var launchFacade = new Mock<ILaunchFacade>();
        launchFacade
            .Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()))
            .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));

        var launchFeedbackPresenter = new Mock<ILaunchFeedbackPresenter>();
        var dialogFactory = new FakeFirewallDialogFactory(isAvailable: true);
        var orchestrator = CreateOrchestrator(
            dialogFactory: dialogFactory,
            launchFacade: launchFacade.Object,
            launchFeedbackPresenter: launchFeedbackPresenter.Object);

        var action = orchestrator.BuildPostWizardAction(
            sid: "S-1-5-21-100-200-300-1001",
            username: "Agent",
            internetRestrictedInWizard: false,
            session: new SessionContext { Database = new AppDatabase() },
            sessionSaver: Mock.Of<IWizardSessionSaver>(),
            toolPath: @"C:\Tools\agent.exe");

        action!(null!);

        launchFacade.Verify(
            f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()),
            Times.Once);
        launchFeedbackPresenter.Verify(
            p => p.ShowMaintenanceWarning(It.IsAny<LaunchExecutionResult>(), It.IsAny<LaunchFeedbackContext>()),
            Times.Once);
        Assert.Null(dialogFactory.LastRequest);
        Assert.Null(dialogFactory.LastDialog);
    }

    [Fact]
    public void PostWizardAction_InternetRestrictedInWizard_UsesDialogFactoryAndPropagatesRollbackAndSave()
    {
        const string sid = "S-1-5-21-100-200-300-1001";
        const string username = "Agent";
        const string toolPath = @"C:\Tools\agent.exe";
        var database = new AppDatabase();
        database.GetOrCreateAccount(sid).Firewall = new FirewallAccountSettings
        {
            AllowInternet = false,
            AllowLan = true,
            AllowLocalhost = false,
            LocalhostPortExemptions = ["8080"],
            FilterEphemeralLoopback = false,
            Allowlist = [new FirewallAllowlistEntry { Value = "api.anthropic.com" }]
        };

        var launchFacade = new Mock<ILaunchFacade>();
        launchFacade
            .Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()))
            .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));

        var launchFeedbackPresenter = new Mock<ILaunchFeedbackPresenter>();
        var firewallApplyHelper = new Mock<IFirewallApplyHelper>();
        firewallApplyHelper
            .Setup(h => h.ApplyWithRollback(
                It.IsAny<IWin32Window?>(),
                sid,
                username,
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<FirewallAccountSettings>(),
                database,
                It.IsAny<Action>()))
            .Returns((IWin32Window? _, string _, string _, FirewallAccountSettings? _, FirewallAccountSettings _, AppDatabase _, Action saveAction) =>
            {
                saveAction();
                return true;
            });

        var dialog = new FakeFirewallAllowlistDialog(
            result: [new FirewallAllowlistEntry { Value = "docs.anthropic.com" }],
            allowInternet: true,
            allowLan: false,
            allowLocalhost: true,
            allowedLocalhostPorts: ["3000", "3001"],
            filterEphemeralLoopback: true)
        {
            RaiseAppliedOnShow = true
        };
        var dialogFactory = new FakeFirewallDialogFactory(isAvailable: true, dialog);
        var sessionSaver = new Mock<IWizardSessionSaver>();
        var orchestrator = CreateOrchestrator(
            firewallApplyHelper: firewallApplyHelper.Object,
            dialogFactory: dialogFactory,
            launchFacade: launchFacade.Object,
            launchFeedbackPresenter: launchFeedbackPresenter.Object);

        var action = orchestrator.BuildPostWizardAction(
            sid: sid,
            username: username,
            internetRestrictedInWizard: true,
            session: new SessionContext { Database = database },
            sessionSaver: sessionSaver.Object,
            toolPath: toolPath);

        action!(null!);

        Assert.NotNull(dialogFactory.LastRequest);
        Assert.Equal(username, dialogFactory.LastRequest!.DisplayName);
        Assert.False(dialogFactory.LastRequest.AllowInternet);
        Assert.True(dialogFactory.LastRequest.AllowLan);
        Assert.False(dialogFactory.LastRequest.AllowLocalhost);
        Assert.Equal(["8080"], dialogFactory.LastRequest.AllowedLocalhostPorts);
        Assert.False(dialogFactory.LastRequest.FilterEphemeralLoopback);
        Assert.Equal(["api.anthropic.com"], dialogFactory.LastRequest.Current.Select(entry => entry.Value));

        Assert.True(dialog.AutoOpenCalled);
        Assert.True(dialog.ShowDialogCalled);
        Assert.True(dialog.LastAppliedArgs?.RolledBack == true);

        firewallApplyHelper.Verify(h => h.ApplyWithRollback(
            It.IsAny<IWin32Window?>(),
            sid,
            username,
            It.IsAny<FirewallAccountSettings?>(),
            It.Is<FirewallAccountSettings>(settings =>
                settings.AllowInternet
                && !settings.AllowLan
                && settings.AllowLocalhost
                && settings.FilterEphemeralLoopback
                && settings.LocalhostPortExemptions.SequenceEqual(new[] { "3000", "3001" })
                && settings.Allowlist.Select(entry => entry.Value).SequenceEqual(new[] { "docs.anthropic.com" })),
            database,
            It.IsAny<Action>()), Times.Once);
        sessionSaver.Verify(s => s.SaveAndRefresh(), Times.Once);
        launchFacade.Verify(
            f => f.LaunchFile(
                It.Is<ProcessLaunchTarget>(target => string.Equals(target.ExePath, toolPath, StringComparison.Ordinal)),
                It.IsAny<LaunchIdentity>(),
                It.IsAny<Func<string, string, bool>?>()),
            Times.Once);
        launchFeedbackPresenter.Verify(
            p => p.ShowMaintenanceWarning(It.IsAny<LaunchExecutionResult>(), It.IsAny<LaunchFeedbackContext>()),
            Times.Once);
    }

    private static AiAgentFirewallOrchestrator CreateOrchestrator(
        IFirewallApplyHelper? firewallApplyHelper = null,
        IFirewallDialogFactory? dialogFactory = null,
        ILaunchFacade? launchFacade = null,
        ILaunchFeedbackPresenter? launchFeedbackPresenter = null)
    {
        return new AiAgentFirewallOrchestrator(
            firewallApplyHelper: firewallApplyHelper ?? Mock.Of<IFirewallApplyHelper>(),
            dialogFactory: dialogFactory ?? new FakeFirewallDialogFactory(isAvailable: true),
            databaseProvider: Mock.Of<IDatabaseProvider>(),
            launchFacade: launchFacade ?? Mock.Of<ILaunchFacade>(),
            launchFeedbackPresenter: launchFeedbackPresenter ?? Mock.Of<ILaunchFeedbackPresenter>(),
            accountToolResolver: null!);
    }

    private sealed record AllowlistDialogRequest(
        List<FirewallAllowlistEntry> Current,
        string? DisplayName,
        bool AllowInternet,
        bool AllowLan,
        bool AllowLocalhost,
        IReadOnlyList<string>? AllowedLocalhostPorts,
        bool FilterEphemeralLoopback);

    private sealed class FakeFirewallDialogFactory(bool isAvailable, IFirewallAllowlistDialog? dialogInstance = null) : IFirewallDialogFactory
    {
        private readonly IFirewallAllowlistDialog? dialogToReturn = dialogInstance;

        public bool IsAvailable { get; } = isAvailable;
        public AllowlistDialogRequest? LastRequest { get; private set; }
        public IFirewallAllowlistDialog? LastDialog { get; private set; }

        public IFirewallAllowlistDialog? CreateAllowlistDialog(
            List<FirewallAllowlistEntry> current,
            string? displayName,
            bool allowInternet,
            bool allowLan,
            bool allowLocalhost,
            IReadOnlyList<string>? allowedLocalhostPorts,
            bool filterEphemeralLoopback = true)
        {
            LastRequest = new AllowlistDialogRequest(
                current,
                displayName,
                allowInternet,
                allowLan,
                allowLocalhost,
                allowedLocalhostPorts,
                filterEphemeralLoopback);
            LastDialog = dialogToReturn;
            return dialogToReturn;
        }
    }

    private sealed class FakeFirewallAllowlistDialog(
        List<FirewallAllowlistEntry> result,
        bool allowInternet,
        bool allowLan,
        bool allowLocalhost,
        IReadOnlyList<string> allowedLocalhostPorts,
        bool filterEphemeralLoopback) : IFirewallAllowlistDialog
    {
        public event EventHandler<FirewallApplyEventArgs>? Applied;

        public bool RaiseAppliedOnShow { get; set; }
        public bool AutoOpenCalled { get; private set; }
        public bool ShowDialogCalled { get; private set; }
        public FirewallApplyEventArgs? LastAppliedArgs { get; private set; }

        public List<FirewallAllowlistEntry> Result { get; } = result;
        public bool AllowInternet { get; } = allowInternet;
        public bool AllowLan { get; } = allowLan;
        public bool AllowLocalhost { get; } = allowLocalhost;
        public IReadOnlyList<string> AllowedLocalhostPorts { get; } = allowedLocalhostPorts;
        public bool FilterEphemeralLoopback { get; } = filterEphemeralLoopback;

        public void AutoOpenBlockedConnectionsOnShow()
        {
            AutoOpenCalled = true;
        }

        public DialogResult ShowDialog(IWin32Window? owner)
        {
            ShowDialogCalled = true;
            if (RaiseAppliedOnShow)
            {
                LastAppliedArgs = new FirewallApplyEventArgs();
                Applied?.Invoke(this, LastAppliedArgs);
            }

            return DialogResult.OK;
        }

        public void Dispose()
        {
        }
    }
}
