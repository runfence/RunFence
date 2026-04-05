using RunFence.Account.UI;
using RunFence.Infrastructure;
using RunFence.Account.UI.AppContainer;
using RunFence.Acl.UI.Forms;
using RunFence.Apps.UI.Forms;
using RunFence.Core.Models;
using RunFence.UI;

namespace RunFence.Apps.UI;

/// <summary>
/// Handles account/container switching state in <see cref="AppEditDialog"/>.
/// Manages saving and restoring prior UI state when switching between regular accounts
/// and AppContainer selections in the account combo box.
/// </summary>
public class AppEditAccountSwitchHandler(ILocalGroupMembershipService groupMembership)
{
    private CheckBox _launchAsLowIlCheckBox = null!;
    private CheckBox _splitTokenCheckBox = null!;
    private AclConfigSection _aclSection = null!;

    // Saves state before it was forced by AppContainer selection,
    // so we can restore it if the user switches back to a user account.
    private AclMode _priorAclMode;

    /// <summary>
    /// Binds the handler to the per-dialog UI controls. Must be called before any operations.
    /// </summary>
    public void Initialize(CheckBox launchAsLowIlCheckBox, CheckBox splitTokenCheckBox, AclConfigSection aclSection)
    {
        _launchAsLowIlCheckBox = launchAsLowIlCheckBox;
        _splitTokenCheckBox = splitTokenCheckBox;
        _aclSection = aclSection;
    }

    /// <summary>
    /// Updates UI state in response to an account combo selection change.
    /// Should be called after separator-skip logic has already been handled.
    /// </summary>
    public void HandleSelectionChanged(object? selectedItem)
    {
        if (selectedItem is AppContainerDisplayItem)
        {
            // Only capture prior values when transitioning FROM a non-container selection;
            // container-to-container switches must not overwrite the saved prior values.
            if (_launchAsLowIlCheckBox.Enabled)
            {
                PriorLaunchAsLowIl = _launchAsLowIlCheckBox.CheckState switch
                {
                    CheckState.Checked => true,
                    CheckState.Unchecked => false,
                    _ => null
                };
                _priorAclMode = _aclSection.AclMode;
            }

            _launchAsLowIlCheckBox.CheckState = CheckState.Checked;
            _launchAsLowIlCheckBox.Enabled = false;
            _splitTokenCheckBox.Enabled = false;

            _aclSection.AclMode = AclMode.Allow;
        }
        else
        {
            if (!_launchAsLowIlCheckBox.Enabled) // was forced by container — restore saved values
            {
                _launchAsLowIlCheckBox.CheckState = PriorLaunchAsLowIl switch
                {
                    true => CheckState.Checked,
                    false => CheckState.Unchecked,
                    null => CheckState.Indeterminate
                };
                _aclSection.AclMode = _priorAclMode;
            }

            _launchAsLowIlCheckBox.Enabled = true;
            if (selectedItem is CredentialDisplayItem credItem)
            {
                var isAdmin = GroupFilterHelper.IsAdminAccount(credItem.Credential.Sid, groupMembership);
                _splitTokenCheckBox.Enabled = isAdmin;
                if (!isAdmin)
                    _splitTokenCheckBox.CheckState = CheckState.Indeterminate;
            }
            else
            {
                _splitTokenCheckBox.Enabled = true;
            }
        }
    }

    /// <summary>
    /// The prior LaunchAsLowIntegrity value saved before container selection forced it on.
    /// Used by the dialog when building the result to preserve the original setting for container apps.
    /// </summary>
    public bool? PriorLaunchAsLowIl { get; private set; }
}