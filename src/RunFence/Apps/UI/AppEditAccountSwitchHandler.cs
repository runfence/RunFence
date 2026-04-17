using RunFence.Account.UI.AppContainer;
using RunFence.Acl.UI.Forms;
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

    // Saves state before it was forced by AppContainer selection,
    // so we can restore it if the user switches back to a user account.
    private AclMode _priorAclMode;
    private bool _wasContainerSelected;

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
            // Only capture prior values when transitioning FROM a non-container selection;
            // container-to-container switches must not overwrite the saved prior values.
            if (!_wasContainerSelected)
            {
                PriorPrivilegeLevel = PrivilegeLevelComboHelper.IndexToMode(_privilegeLevelComboBox.SelectedIndex);
                _priorAclMode = _aclSection.AclMode;
            }

            _privilegeLevelComboBox.SelectedIndex = PrivilegeLevelComboHelper.ModeToIndex(PrivilegeLevel.LowIntegrity);
            _privilegeLevelComboBox.Enabled = false;
            _wasContainerSelected = true;

            _aclSection.AclMode = AclMode.Allow;
        }
        else
        {
            if (_wasContainerSelected) // was forced by container — restore saved values
            {
                _privilegeLevelComboBox.SelectedIndex = PrivilegeLevelComboHelper.ModeToIndex(PriorPrivilegeLevel);
                _aclSection.AclMode = _priorAclMode;
            }

            _privilegeLevelComboBox.Enabled = true;
            _wasContainerSelected = false;
        }
    }

    /// <summary>
    /// The prior PrivilegeLevel saved before container selection forced it to LowIntegrity.
    /// Used by the controller when building the result to preserve the original setting for container apps.
    /// </summary>
    public PrivilegeLevel? PriorPrivilegeLevel { get; private set; }
}