using Moq;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Firewall.UI;
using RunFence.Licensing;
using Xunit;

namespace RunFence.Tests;

public sealed class FirewallAllowlistDialogCoordinatorTests
{
    private static readonly TimeSpan ApplyTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void HandleSelectedTabChanged_UpdatesToolbarForEachTab()
    {
        var fixture = CreateFixture();

        fixture.View.IsInternetTabSelected = true;
        fixture.View.SelectedAllowlistRowCount = 2;
        fixture.Coordinator.HandleSelectedTabChanged();

        Assert.True(fixture.View.AddEnabled);
        Assert.Equal("Add entry (IP, CIDR, or domain - auto-detected)", fixture.View.AddToolTipText);
        Assert.True(fixture.View.RemoveEnabled);
        Assert.Equal("Remove selected entries", fixture.View.RemoveToolTipText);
        Assert.True(fixture.View.ResolveEnabled);
        Assert.True(fixture.View.ViewBlockedEnabled);

        fixture.View.IsInternetTabSelected = false;
        fixture.View.SelectedPortRowCount = 1;
        fixture.Coordinator.HandleSelectedTabChanged();

        Assert.True(fixture.View.AddEnabled);
        Assert.Equal("Add port exception", fixture.View.AddToolTipText);
        Assert.True(fixture.View.RemoveEnabled);
        Assert.Equal("Remove selected ports", fixture.View.RemoveToolTipText);
        Assert.False(fixture.View.ResolveEnabled);
        Assert.False(fixture.View.ViewBlockedEnabled);
    }

    [Fact]
    public async Task ApplyAsync_Success_CommitsHandlersAndClearsPendingInput()
    {
        var fixture = CreateFixture();
        fixture.View.AllowLanChecked = false;

        await fixture.Coordinator.ApplyAsync(CancellationToken.None);

        Assert.False(fixture.View.ApplyButtonEnabled);
        Assert.False(fixture.LastAppliedEventArgs!.RolledBack);
        Assert.False(fixture.AllowlistHandler.HasUnappliedChanges());
        Assert.False(fixture.PortsHandler.HasUnappliedChanges());
    }

    [Fact]
    public async Task ApplyAsync_RolledBack_RetainsPendingInput()
    {
        var fixture = CreateFixture(args => args.RolledBack = true);
        fixture.View.AllowLanChecked = false;

        await fixture.Coordinator.ApplyAsync(CancellationToken.None);

        Assert.True(fixture.View.ApplyButtonEnabled);
    }

    [Fact]
    public async Task ApplyAsync_InFlight_DisablesApplyButton()
    {
        var applyEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowApplyToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int applyCountAtStart = 0;
        int interactionCountAtStart = 0;

        CoordinatorFixture? fixture = null;
        fixture = CreateFixture(args =>
        {
            var applyCountBeforeEnter = applyCountAtStart;
            var interactionCountBeforeEnter = interactionCountAtStart;

            applyEntered.SetResult();
            Assert.True(applyCountBeforeEnter < fixture!.View.SetApplyButtonEnabledStateHistory.Count);
            Assert.True(interactionCountBeforeEnter < fixture.View.SetInteractionEnabledStateHistory.Count);
            Assert.False(fixture.View.SetApplyButtonEnabledStateHistory[^1]);
            Assert.False(fixture.View.SetInteractionEnabledStateHistory[^1].enabled);
            allowApplyToFinish.Task.WaitAsync(ApplyTimeout).GetAwaiter().GetResult();
        });

        applyCountAtStart = fixture.View.SetApplyButtonEnabledStateHistory.Count;
        interactionCountAtStart = fixture.View.SetInteractionEnabledStateHistory.Count;
        fixture.View.AllowLanChecked = false;

        var applyTask = Task.Run(() => fixture.Coordinator.ApplyAsync(CancellationToken.None));

        try
        {
            await applyEntered.Task.WaitAsync(ApplyTimeout);
            Assert.False(fixture.View.SetApplyButtonEnabledStateHistory[^1]);
            Assert.False(fixture.View.SetInteractionEnabledStateHistory[^1].enabled);
            Assert.False(fixture.View.ApplyButtonEnabled);
        }
        finally
        {
            allowApplyToFinish.TrySetResult();
        }

        await applyTask.WaitAsync(ApplyTimeout);

        Assert.False(fixture.View.ApplyButtonEnabled);
    }

    [Fact]
    public void ConfirmCloseWithPendingChanges_UsesPromptDecision()
    {
        var fixture = CreateFixture();
        fixture.View.AllowLanChecked = false;
        fixture.View.DiscardPromptResult = DialogResult.No;

        Assert.False(fixture.Coordinator.ConfirmCloseWithPendingChanges());

        fixture.View.DiscardPromptResult = DialogResult.Yes;

        Assert.True(fixture.Coordinator.ConfirmCloseWithPendingChanges());
    }

    [Fact]
    public void HandleShortcutKey_EscapeRequestsClose()
    {
        var fixture = CreateFixture();

        var handled = fixture.Coordinator.HandleShortcutKey(Keys.Escape);

        Assert.True(handled);
        Assert.True(fixture.View.CloseRequested);
    }

    private static CoordinatorFixture CreateFixture(
        Action<FirewallApplyEventArgs>? applyAction = null)
    {
        var licenseService = new Mock<ILicenseService>();
        licenseService.Setup(service => service.CanAddFirewallAllowlistEntry(It.IsAny<int>())).Returns(false);
        licenseService
            .Setup(service => service.GetRestrictionMessage(EvaluationFeature.FirewallAllowlist, It.IsAny<int>()))
            .Returns("Firewall allowlist license limit reached.");
        var validator = new FirewallAllowlistValidator(licenseService.Object);
        var allowlistHandler = new FirewallAllowlistTabHandler(
            validator,
            new FirewallDomainResolver(Mock.Of<IFirewallNetworkInfo>(), Mock.Of<IDnsResolver>()),
            []);
        var portsHandler = new FirewallPortsTabHandler(new FirewallPortValidator(), null);
        var view = new FakeFirewallAllowlistDialogView();
        FirewallApplyEventArgs? capturedArgs = null;

        var firewallNetworkInfo = new Mock<IFirewallNetworkInfo>();
        firewallNetworkInfo
            .Setup(info => info.GetDnsServerAddresses())
            .Returns(new List<string> { "192.0.2.53" });

        var coordinator = new FirewallAllowlistDialogCoordinator(
            view,
            firewallNetworkInfo.Object,
            allowlistHandler,
            portsHandler,
            new FirewallDialogApplyPresenter(),
            initialAllowInternet: true,
            initialAllowLan: true,
            initialAllowLocalhost: true,
            initialFilterEphemeralLoopback: true);

        view.AppliedAction = args =>
        {
            capturedArgs = args;
            applyAction?.Invoke(args);
        };

        coordinator.Initialize();
        return new CoordinatorFixture(
            view,
            coordinator,
            allowlistHandler,
            portsHandler,
            () => capturedArgs);
    }

    private sealed record CoordinatorFixture(
        FakeFirewallAllowlistDialogView View,
        FirewallAllowlistDialogCoordinator Coordinator,
        FirewallAllowlistTabHandler AllowlistHandler,
        FirewallPortsTabHandler PortsHandler,
        Func<FirewallApplyEventArgs?> GetLastAppliedEventArgs)
    {
        public FirewallApplyEventArgs? LastAppliedEventArgs => GetLastAppliedEventArgs();
    }

    private sealed class FakeFirewallAllowlistDialogView : IFirewallAllowlistDialogView
    {
        public bool IsInternetTabSelected { get; set; } = true;
        public bool IsResolvingDomains { get; set; }
        public int SelectedAllowlistRowCount { get; set; }
        public int SelectedPortRowCount { get; set; }
        public bool AllowInternetChecked { get; set; } = true;
        public bool AllowLanChecked { get; set; } = true;
        public bool AllowLocalhostChecked { get; set; } = true;
        public bool FilterEphemeralChecked { get; set; } = true;
        public DataGridView AllowlistGrid { get; } = new();
        public DataGridView PortsGrid { get; } = new();
        public bool AddEnabled { get; private set; }
        public string AddToolTipText { get; private set; } = "";
        public bool RemoveEnabled { get; private set; }
        public string RemoveToolTipText { get; private set; } = "";
        public bool ResolveEnabled { get; private set; }
        public bool ViewBlockedEnabled { get; private set; }
        public bool ApplyButtonEnabled { get; private set; }
        public bool CloseRequested { get; private set; }
        public DialogResult DiscardPromptResult { get; set; } = DialogResult.Yes;
        public string? LastInfoTitle { get; private set; }
        public string? LastInfoMessage { get; private set; }
        public string? LastWarningTitle { get; private set; }
        public string? LastWarningMessage { get; private set; }
        public string? LastErrorTitle { get; private set; }
        public string? LastErrorMessage { get; private set; }
        public Action<FirewallApplyEventArgs>? AppliedAction { get; set; }
        public IntPtr Handle => IntPtr.Zero;

        public string? PromptInput(string title, string prompt) => null;

        public void SetDnsLabelText(string text)
        {
        }

        public void SetFilterEphemeralEnabled(bool enabled)
        {
        }

        public void SetWarningVisibility(bool internetWarningVisible, bool portsWarningVisible)
        {
        }

        public void SetToolbarState(
            bool addEnabled,
            string addToolTipText,
            bool removeEnabled,
            string removeToolTipText,
            string exportToolTipText,
            bool resolveEnabled,
            bool viewBlockedEnabled)
        {
            AddEnabled = addEnabled;
            AddToolTipText = addToolTipText;
            RemoveEnabled = removeEnabled;
            RemoveToolTipText = removeToolTipText;
            ResolveEnabled = resolveEnabled;
            ViewBlockedEnabled = viewBlockedEnabled;
        }

        public List<bool> SetApplyButtonEnabledStateHistory { get; } = [];

        public List<(bool enabled, bool filterEphemeralEnabled)> SetInteractionEnabledStateHistory { get; } = [];

        public void SetInteractionEnabled(bool enabled, bool filterEphemeralEnabled)
        {
            SetInteractionEnabledStateHistory.Add((enabled, filterEphemeralEnabled));
        }

        public void SetApplyButtonEnabled(bool enabled)
        {
            ApplyButtonEnabled = enabled;
            SetApplyButtonEnabledStateHistory.Add(enabled);
        }

        public void CommitGridEdits()
        {
        }

        public void RaiseApplied(FirewallApplyEventArgs args) => AppliedAction?.Invoke(args);

        public void SetAppliedValues(
            List<FirewallAllowlistEntry> result,
            bool allowInternet,
            bool allowLan,
            bool allowLocalhost,
            IReadOnlyList<string> allowedLocalhostPorts,
            bool filterEphemeralLoopback)
        {
            AllowInternetChecked = allowInternet;
            AllowLanChecked = allowLan;
            AllowLocalhostChecked = allowLocalhost;
            FilterEphemeralChecked = filterEphemeralLoopback;
        }

        public DialogResult ShowDiscardChangesPrompt() => DiscardPromptResult;

        public void ShowInformation(string title, string message)
        {
            LastInfoTitle = title;
            LastInfoMessage = message;
        }

        public void ShowWarning(string title, string message)
        {
            LastWarningTitle = title;
            LastWarningMessage = message;
        }

        public void ShowError(string title, string message)
        {
            LastErrorTitle = title;
            LastErrorMessage = message;
        }

        public void RequestClose() => CloseRequested = true;

        public void UpdateApplyButton()
        {
        }

        public void RefreshToolbarState()
        {
        }
    }

}
