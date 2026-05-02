using RunFence.Account.UI;
using RunFence.Account.UI.AppContainer;
using RunFence.Acl.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Apps.UI;

/// <summary>
/// Handles account/container switching state in <see cref="AppEditDialog"/>.
/// Manages saving and restoring prior UI state when switching between regular accounts
/// and AppContainer selections in the account combo box.
/// </summary>
public class AppEditAccountSwitchHandler
{
    private ComboBox _privilegeLevelComboBox = null!;
    private AclConfigSection _aclSection = null!;

    // Saves state before it was forced by AppContainer or SYSTEM selection,
    // so we can restore it if the user switches back to a regular account.
    private AclMode _priorAclMode;
    private bool _wasContainerSelected;
    private bool _wasSystemSelected;

    /// <summary>
    /// Binds the handler to the per-dialog UI controls. Must be called before any operations.
    /// </summary>
    public void Initialize(ComboBox privilegeLevelComboBox, AclConfigSection aclSection)
    {
        _privilegeLevelComboBox = privilegeLevelComboBox;
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
            // Only capture prior values when transitioning FROM a non-container, non-SYSTEM selection;
            // forced-to-forced switches must not overwrite the saved prior values.
            if (!_wasContainerSelected && !_wasSystemSelected)
            {
                PriorPrivilegeLevel = PrivilegeLevelComboHelper.IndexToMode(_privilegeLevelComboBox.SelectedIndex);
                _priorAclMode = _aclSection.AclMode;
            }

            _privilegeLevelComboBox.SelectedIndex = PrivilegeLevelComboHelper.ModeToIndex(PrivilegeLevel.LowIntegrity);
            _privilegeLevelComboBox.Enabled = false;
            _wasContainerSelected = true;
            _wasSystemSelected = false;

            _aclSection.AclMode = AclMode.Allow;
        }
        else if (selectedItem is CredentialDisplayItem cdi && SidResolutionHelper.IsSystemSid(cdi.Credential.Sid))
        {
            if (!_wasContainerSelected && !_wasSystemSelected)
            {
                PriorPrivilegeLevel = PrivilegeLevelComboHelper.IndexToMode(_privilegeLevelComboBox.SelectedIndex);
                _priorAclMode = _aclSection.AclMode;
            }

            // Lock to Account default — SYSTEM's privilege level is an OS invariant (HighestAllowed).
            _privilegeLevelComboBox.SelectedIndex = PrivilegeLevelComboHelper.ModeToIndex(null);
            _privilegeLevelComboBox.Enabled = false;
            _wasSystemSelected = true;
            _wasContainerSelected = false;
        }
        else
        {
            if (_wasContainerSelected || _wasSystemSelected) // was forced — restore saved values
            {
                _privilegeLevelComboBox.SelectedIndex = PrivilegeLevelComboHelper.ModeToIndex(PriorPrivilegeLevel);
                _aclSection.AclMode = _priorAclMode;
            }

            _privilegeLevelComboBox.Enabled = true;
            _wasContainerSelected = false;
            _wasSystemSelected = false;
        }
    }

    /// <summary>
    /// The prior PrivilegeLevel saved before a container or SYSTEM selection forced a locked value.
    /// Used by the controller when building the result to preserve the original setting for container apps.
    /// </summary>
    public PrivilegeLevel? PriorPrivilegeLevel { get; private set; }
}