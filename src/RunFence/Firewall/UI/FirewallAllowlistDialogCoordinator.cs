using RunFence.Core.Models;

namespace RunFence.Firewall.UI;

public sealed class FirewallAllowlistDialogCoordinator(
    IFirewallAllowlistDialogView view,
    IFirewallNetworkInfo firewallNetworkInfo,
    FirewallAllowlistTabHandler allowlistHandler,
    FirewallPortsTabHandler portsHandler,
    FirewallDialogApplyPresenter applyPresenter,
    bool initialAllowInternet,
    bool initialAllowLan,
    bool initialAllowLocalhost,
    bool initialFilterEphemeralLoopback)
{
    private bool _initialAllowInternet = initialAllowInternet;
    private bool _initialAllowLan = initialAllowLan;
    private bool _initialAllowLocalhost = initialAllowLocalhost;
    private bool _initialFilterEphemeralLoopback = initialFilterEphemeralLoopback;
    private bool _isApplying;

    public void Initialize()
    {
        UpdateDnsLabel();
        HandleFirewallSettingsChanged();
        HandleSelectedTabChanged();
    }

    public void HandleFirewallSettingsChanged()
    {
        view.SetFilterEphemeralEnabled(!view.AllowLocalhostChecked);
        view.SetWarningVisibility(
            internetWarningVisible: view.AllowInternetChecked && view.AllowLanChecked,
            portsWarningVisible: view.AllowLocalhostChecked);
        UpdateApplyButtonState();
    }

    public void HandleSelectedTabChanged()
    {
        bool internetTab = view.IsInternetTabSelected;
        view.SetToolbarState(
            addEnabled: !internetTab || !view.IsResolvingDomains,
            addToolTipText: internetTab
                ? "Add entry (IP, CIDR, or domain - auto-detected)"
                : "Add port exception",
            removeEnabled: internetTab
                ? view.SelectedAllowlistRowCount > 0
                : view.SelectedPortRowCount > 0,
            removeToolTipText: internetTab
                ? "Remove selected entries"
                : "Remove selected ports",
            exportToolTipText: internetTab
                ? "Export selected entries to file (exports all entries and ports when nothing is selected)"
                : "Export selected ports to file (exports all entries and ports when nothing is selected)",
            resolveEnabled: internetTab && !view.IsResolvingDomains,
            viewBlockedEnabled: internetTab);
    }

    public void UpdateApplyButtonState()
    {
        view.SetApplyButtonEnabled(!_isApplying && HasUnappliedChanges());
    }

    public Task ApplyAsync(CancellationToken cancellationToken)
    {
        if (_isApplying)
            return Task.CompletedTask;

        cancellationToken.ThrowIfCancellationRequested();
        view.CommitGridEdits();
        view.SetAppliedValues(
            allowlistHandler.GetEntries().ToList(),
            view.AllowInternetChecked,
            view.AllowLanChecked,
            view.AllowLocalhostChecked,
            portsHandler.GetPortEntries().ToList(),
            view.FilterEphemeralChecked);

        _isApplying = true;
        UpdateApplyButtonState();
        view.SetInteractionEnabled(enabled: false, filterEphemeralEnabled: false);
        try
        {
            var args = new FirewallApplyEventArgs();
            view.RaiseApplied(args);

            var presentation = applyPresenter.Present(args.RolledBack, changedSettingsCount: 1);
            if (presentation.RetainPendingInput)
                return Task.CompletedTask;

            _initialAllowInternet = view.AllowInternetChecked;
            _initialAllowLan = view.AllowLanChecked;
            _initialAllowLocalhost = view.AllowLocalhostChecked;
            _initialFilterEphemeralLoopback = view.FilterEphemeralChecked;
            allowlistHandler.CommitApply();
            portsHandler.CommitApply();
        }
        finally
        {
            _isApplying = false;
            view.SetInteractionEnabled(
                enabled: true,
                filterEphemeralEnabled: !view.AllowLocalhostChecked);
            UpdateApplyButtonState();
        }

        return Task.CompletedTask;
    }

    public bool ConfirmCloseWithPendingChanges()
    {
        if (_isApplying)
            return false;

        if (!HasUnappliedChanges())
            return true;

        return view.ShowDiscardChangesPrompt() == DialogResult.Yes;
    }

    public bool HandleShortcutKey(Keys keyData)
    {
        if (keyData != Keys.Escape)
            return false;

        if (_isApplying)
            return true;

        view.RequestClose();
        return true;
    }

    private bool HasUnappliedChanges() =>
        view.AllowInternetChecked != _initialAllowInternet ||
        view.AllowLanChecked != _initialAllowLan ||
        view.AllowLocalhostChecked != _initialAllowLocalhost ||
        view.FilterEphemeralChecked != _initialFilterEphemeralLoopback ||
        allowlistHandler.HasUnappliedChanges() ||
        portsHandler.HasUnappliedChanges();

    private void UpdateDnsLabel()
    {
        try
        {
            var servers = firewallNetworkInfo.GetDnsServerAddresses();
            view.SetDnsLabelText(servers.Count > 0
                ? $"DNS servers (auto-included when allowlist is non-empty): {string.Join(", ", servers)}"
                : "DNS servers: none detected");
        }
        catch
        {
            view.SetDnsLabelText("DNS servers: unavailable");
        }
    }
}
