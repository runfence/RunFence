using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Account.UI.AppContainer;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Container;
using RunFence.RunAs.UI.Forms;
using RunFence.UI;

namespace RunFence.RunAs.UI;

/// <summary>
/// Handles populating and restoring selection in the credential list box for <see cref="RunAsDialog"/>.
/// </summary>
public class RunAsCredentialListPopulator(
    ISidResolver sidResolver,
    IWindowsAccountService windowsAccountService,
    ILocalUserProvider localUserProvider,
    IAppContainerService appContainerService)
{
    private ListBox _listBox = null!;
    private List<CredentialEntry> _credentials = null!;
    private IReadOnlyDictionary<string, string>? _sidNames;
    private CheckBox _showAllAccountsCheckBox = null!;
    private string? _currentUserSid;
    private List<AppContainerEntry>? _appContainers;

    /// <summary>
    /// Binds the handler to per-use dialog controls and data. Must be called before any operations.
    /// </summary>
    public void Initialize(
        ListBox listBox,
        List<CredentialEntry> credentials,
        IReadOnlyDictionary<string, string>? sidNames,
        CheckBox showAllAccountsCheckBox,
        string? currentUserSid,
        List<AppContainerEntry>? appContainers)
    {
        _listBox = listBox;
        _credentials = credentials;
        _sidNames = sidNames;
        _showAllAccountsCheckBox = showAllAccountsCheckBox;
        _currentUserSid = currentUserSid;
        _appContainers = appContainers;
    }

    public void Repopulate()
    {
        // Save selected SID / container name before clearing
        string? selectedSid = (_listBox.SelectedItem as CredentialDisplayItem)?.Credential.Sid;
        string? selectedContainerName = (_listBox.SelectedItem as AppContainerDisplayItem)?.Container.Name;
        bool selectedCreate = _listBox.SelectedItem is CreateAccountItem;

        _listBox.Items.Clear();

        var representedSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cred in CredentialFilterHelper.FilterResolvableCredentials(_credentials, _sidNames, sidResolver))
        {
            _listBox.Items.Add(new CredentialDisplayItem(cred, sidResolver, _sidNames));
            if (!string.IsNullOrEmpty(cred.Sid))
                representedSids.Add(cred.Sid);
        }

        if (_showAllAccountsCheckBox.Checked)
        {
            var interactiveSid = SidResolutionHelper.GetInteractiveUserSid();
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
                    if (windowsAccountService.IsAccountDisabled(user.Username))
                        continue;

                    var transient = new CredentialEntry { Id = Guid.NewGuid(), Sid = user.Sid };
                    _listBox.Items.Add(new CredentialDisplayItem(transient, sidResolver, _sidNames, hasStoredCredential: false));
                    representedSids.Add(user.Sid);
                }
            }
            catch
            {
                /* ignore enumeration errors */
            }
        }

        var interactiveSidFull = SidResolutionHelper.GetInteractiveUserSid();
        if (interactiveSidFull != null && !representedSids.Contains(interactiveSidFull))
        {
            var transient = new CredentialEntry { Id = Guid.NewGuid(), Sid = interactiveSidFull };
            _listBox.Items.Add(new CredentialDisplayItem(transient, sidResolver, _sidNames));
        }

        _listBox.Items.Add(new CreateAccountItem());

        _listBox.Items.Add(new ContainerSeparatorItem());
        if (_appContainers != null)
        {
            foreach (var container in _appContainers.OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                string containerSid;
                try
                {
                    containerSid = appContainerService.GetSid(container.Name);
                }
                catch
                {
                    containerSid = "";
                }

                _listBox.Items.Add(new AppContainerDisplayItem(container, containerSid));
            }
        }

        _listBox.Items.Add(new CreateContainerItem());

        // Restore selection
        if (selectedSid != null)
        {
            var idx = FindItemIndex(item => item is CredentialDisplayItem di &&
                                            string.Equals(di.Credential.Sid, selectedSid, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                _listBox.SelectedIndex = idx;
                return;
            }
        }

        if (selectedContainerName != null)
        {
            var idx = FindItemIndex(item => item is AppContainerDisplayItem acdi &&
                                            string.Equals(acdi.Container.Name, selectedContainerName, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                _listBox.SelectedIndex = idx;
                return;
            }
        }

        if (selectedCreate)
        {
            var idx = FindItemIndex(item => item is CreateAccountItem);
            if (idx >= 0)
                _listBox.SelectedIndex = idx;
        }
    }

    private int FindItemIndex(Func<object, bool> predicate)
    {
        for (int i = 0; i < _listBox.Items.Count; i++)
            if (predicate(_listBox.Items[i]))
                return i;
        return -1;
    }
}