using RunFence.Core.Models;

namespace RunFence.Firewall.UI;

public interface IFirewallAllowlistDialogView : IWin32Window
{
    bool IsInternetTabSelected { get; }
    bool IsResolvingDomains { get; }
    int SelectedAllowlistRowCount { get; }
    int SelectedPortRowCount { get; }
    bool AllowInternetChecked { get; }
    bool AllowLanChecked { get; }
    bool AllowLocalhostChecked { get; }
    bool FilterEphemeralChecked { get; }
    DataGridView AllowlistGrid { get; }
    DataGridView PortsGrid { get; }

    string? PromptInput(string title, string prompt);
    void SetDnsLabelText(string text);
    void SetFilterEphemeralEnabled(bool enabled);
    void SetWarningVisibility(bool internetWarningVisible, bool portsWarningVisible);
    void SetToolbarState(
        bool addEnabled,
        string addToolTipText,
        bool removeEnabled,
        string removeToolTipText,
        string exportToolTipText,
        bool resolveEnabled,
        bool viewBlockedEnabled);
    void SetInteractionEnabled(bool enabled, bool filterEphemeralEnabled);
    void SetApplyButtonEnabled(bool enabled);
    void CommitGridEdits();
    void RaiseApplied(FirewallApplyEventArgs args);
    void SetAppliedValues(
        List<FirewallAllowlistEntry> result,
        bool allowInternet,
        bool allowLan,
        bool allowLocalhost,
        IReadOnlyList<string> allowedLocalhostPorts,
        bool filterEphemeralLoopback);
    DialogResult ShowDiscardChangesPrompt();
    void ShowInformation(string title, string message);
    void ShowWarning(string title, string message);
    void ShowError(string title, string message);
    void RequestClose();
    void UpdateApplyButton();
    void RefreshToolbarState();
}
