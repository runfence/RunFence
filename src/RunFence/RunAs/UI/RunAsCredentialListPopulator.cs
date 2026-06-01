using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Account.UI.AppContainer;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.RunAs.UI.Forms;
using RunFence.UI;

namespace RunFence.RunAs.UI;

/// <summary>
/// Handles populating and restoring selection in the credential list box for <see cref="RunAsDialog"/>.
/// </summary>
public class RunAsCredentialListPopulator(
    CredentialDisplayItemFactory displayItemFactory,
    ILocalUserProvider localUserProvider,
    CredentialFilterHelper credentialFilterHelper)
{
    private readonly CredentialDisplayItemFactory _displayItemFactory = displayItemFactory;

    private ListBox _listBox = null!;
    private List<CredentialEntry> _credentials = null!;
    private IReadOnlyDictionary<string, string>? _sidNames;
    private CheckBox _showAllAccountsCheckBox = null!;
    private string? _currentUserSid;
    private string? _initialAccountSid;
    private List<AppContainerEntry>? _appContainers;
    private bool _showSystemAccount;

    /// <summary>
    /// Binds the handler to per-use dialog controls and data. Must be called before any operations.
    /// </summary>
    public void Initialize(
        ListBox listBox,
        List<CredentialEntry> credentials,
        IReadOnlyDictionary<string, string>? sidNames,
        CheckBox showAllAccountsCheckBox,
        string? currentUserSid,
        string? initialAccountSid,
        List<AppContainerEntry>? appContainers,
        bool showSystemAccount = false)
    {
        _listBox = listBox;
        _credentials = credentials;
        _sidNames = sidNames;
        _showAllAccountsCheckBox = showAllAccountsCheckBox;
        _currentUserSid = currentUserSid;
        _initialAccountSid = initialAccountSid;
        _appContainers = appContainers;
        _showSystemAccount = showSystemAccount;
    }

    public void Repopulate()
    {
        // Save selected SID / container name before clearing
        var selectedItem = _listBox.SelectedItem as RunAsAccountListItem;
        string? selectedSid = (selectedItem?.OptionSource as CredentialRunAsOptionSource)?.Credential.Sid;
        string? selectedContainerName = (selectedItem?.OptionSource as AppContainerRunAsOptionSource)?.Container.Name;
        bool selectedCreateAccount = selectedItem?.OptionSource is CreateAccountRunAsOptionSource;
        bool selectedCreateContainer = selectedItem?.OptionSource is CreateContainerRunAsOptionSource;

        _listBox.Items.Clear();

        var representedSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (_showSystemAccount)
        {
            var displayItem = _displayItemFactory.Create(
                _credentials,
                Core.SidConstants.SystemSid,
                _sidNames,
                isEphemeral: false,
                hasStoredCredentialOverride: false);
            AddListItem(
                displayItem,
                new CredentialRunAsOptionSource(_listBox.Items.Count, displayItem.ToString(), displayItem.Credential));
            representedSids.Add(Core.SidConstants.SystemSid);
        }

        foreach (var cred in credentialFilterHelper.FilterResolvableCredentials(_credentials, _sidNames))
        {
            var displayItem = _displayItemFactory.Create(cred, _sidNames);
            AddListItem(
                displayItem,
                new CredentialRunAsOptionSource(_listBox.Items.Count, displayItem.ToString(), displayItem.Credential));
            if (!string.IsNullOrEmpty(cred.Sid))
                representedSids.Add(cred.Sid);
        }

        var interactiveSid = SidResolutionHelper.GetInteractiveUserSid();

        if (_showAllAccountsCheckBox.Checked)
        {
            try
            {
                var localUsers = localUserProvider.GetLocalUserAccounts();
                foreach (var user in localUsers.OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase))
                {
                    if (representedSids.Contains(user.Sid))
                        continue;
                    if (string.Equals(user.Sid, _currentUserSid, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (interactiveSid != null && string.Equals(user.Sid, interactiveSid, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var displayItem = _displayItemFactory.Create(
                        _credentials,
                        user.Sid,
                        _sidNames,
                        isEphemeral: false,
                        hasStoredCredentialOverride: false);
                    AddListItem(
                        displayItem,
                        new CredentialRunAsOptionSource(_listBox.Items.Count, displayItem.ToString(), displayItem.Credential));
                    representedSids.Add(user.Sid);
                }
            }
            catch
            {
                /* ignore enumeration errors */
            }
        }

        if (interactiveSid != null && !representedSids.Contains(interactiveSid))
        {
            var displayItem = _displayItemFactory.Create(_credentials, interactiveSid, _sidNames);
            AddListItem(
                displayItem,
                new CredentialRunAsOptionSource(_listBox.Items.Count, displayItem.ToString(), displayItem.Credential));
            representedSids.Add(interactiveSid);
        }

        if (!string.IsNullOrEmpty(_initialAccountSid) && !representedSids.Contains(_initialAccountSid))
        {
            var displayItem = _displayItemFactory.Create(
                _credentials,
                _initialAccountSid,
                _sidNames,
                isEphemeral: false,
                hasStoredCredentialOverride: false);
            AddListItem(
                displayItem,
                new CredentialRunAsOptionSource(_listBox.Items.Count, displayItem.ToString(), displayItem.Credential));
        }

        var createAccountItem = new CreateAccountItem();
        AddListItem(
            createAccountItem,
            new CreateAccountRunAsOptionSource(_listBox.Items.Count, createAccountItem.ToString()));

        AddListItem(new ContainerSeparatorItem(), optionSource: null, isSeparator: true);
        if (_appContainers != null)
        {
            foreach (var container in _appContainers.OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                var displayItem = new AppContainerDisplayItem(container, container.Sid);
                AddListItem(
                    displayItem,
                    new AppContainerRunAsOptionSource(
                        _listBox.Items.Count,
                        displayItem.ToString(),
                        displayItem.Container,
                        displayItem.ContainerSid));
            }
        }

        var createContainerItem = new CreateContainerItem();
        AddListItem(
            createContainerItem,
            new CreateContainerRunAsOptionSource(_listBox.Items.Count, createContainerItem.ToString()));

        // Restore selection
        if (selectedSid != null)
        {
            var idx = FindItemIndex(listItem =>
                listItem.OptionSource is CredentialRunAsOptionSource source
                && string.Equals(source.Credential.Sid, selectedSid, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                _listBox.SelectedIndex = idx;
                return;
            }
        }

        if (selectedContainerName != null)
        {
            var idx = FindItemIndex(listItem =>
                listItem.OptionSource is AppContainerRunAsOptionSource source
                && string.Equals(source.Container.Name, selectedContainerName, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                _listBox.SelectedIndex = idx;
                return;
            }
        }

        if (selectedCreateAccount)
        {
            var idx = FindItemIndex(listItem => listItem.OptionSource is CreateAccountRunAsOptionSource);
            if (idx >= 0)
            {
                _listBox.SelectedIndex = idx;
                return;
            }
        }

        if (selectedCreateContainer)
        {
            var idx = FindItemIndex(listItem => listItem.OptionSource is CreateContainerRunAsOptionSource);
            if (idx >= 0)
                _listBox.SelectedIndex = idx;
        }
    }

    private void AddListItem(object displayItem, RunAsAccountOptionSource? optionSource, bool isSeparator = false)
    {
        _listBox.Items.Add(new RunAsAccountListItem(
            displayItem,
            optionSource?.DisplayText ?? displayItem.ToString() ?? string.Empty,
            optionSource,
            isSeparator));
    }

    private int FindItemIndex(Func<RunAsAccountListItem, bool> predicate)
    {
        for (int i = 0; i < _listBox.Items.Count; i++)
            if (_listBox.Items[i] is RunAsAccountListItem listItem && predicate(listItem))
                return i;
        return -1;
    }
}
