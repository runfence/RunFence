using RunFence.Account.UI;
using RunFence.Account.UI.AppContainer;
using RunFence.Acl.UI.Forms;
using RunFence.Apps.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using RunFence.UI;
using RunFence.UI.Forms;

namespace RunFence.Apps.UI;

/// <summary>
/// Handles PopulateFromExisting and OnOkClick validation/build logic for <see cref="AppEditDialog"/>.
/// Decouples complex data-mapping and validation from the dialog form class.
/// </summary>
public class AppEditDialogController(AppEntryBuilder entryBuilder, ISidResolver sidResolver)
{
    private AppEditAccountSwitchHandler _switchHandler = null!;

    /// <summary>
    /// Binds the controller to the per-dialog switch handler. Must be called before any operations.
    /// </summary>
    public void Initialize(AppEditAccountSwitchHandler switchHandler)
    {
        _switchHandler = switchHandler;
    }

    /// <summary>
    /// Populates non-combo dialog controls from an existing app entry.
    /// ACL section, env vars, and IPC callers are populated here.
    /// Account/config combo selection is NOT done here — the caller must apply
    /// PrivilegeLevel to the combobox before selecting the account combo
    /// (the switch handler captures prior privilege level on container selection).
    /// </summary>
    public PopulateFromExistingResult PopulateNonComboState(
        AppEntry app,
        AclConfigSection aclSection,
        IpcCallerSection ipcSection,
        EnvVarsSection envVarsSection)
    {
        aclSection.PopulateFromExisting(app);

        if (app.EnvironmentVariables?.Count > 0)
            envVarsSection.SetItems(app.EnvironmentVariables);

        bool overrideIpcCallers = false;
        if (app.AllowedIpcCallers != null)
        {
            overrideIpcCallers = true;
            ipcSection.SetCallers(app.AllowedIpcCallers);
            ipcSection.SetEnabled(true);
        }

        return new PopulateFromExistingResult(OverrideIpcCallers: overrideIpcCallers,
            SelectedPrivilegeLevel: app.PrivilegeLevel);
    }

    /// <summary>
    /// Selects the account combo item matching the app entry.
    /// Must be called AFTER PrivilegeLevel combobox is set
    /// (the switch handler captures the prior mode on container selection).
    /// </summary>
    public void SelectAccountComboForExisting(
        AppEntry app,
        IReadOnlyDictionary<string, string>? sidNames,
        ComboBox accountComboBox)
    {
        accountComboBox.SelectedIndex = -1;

        if (app.AppContainerName != null)
        {
            AppEditDialogPopulator.SelectComboItem<AppContainerDisplayItem>(accountComboBox,
                ci => string.Equals(ci.Container.Name, app.AppContainerName, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            var found = AppEditDialogPopulator.SelectComboItem<CredentialDisplayItem>(accountComboBox,
                item => string.Equals(item.Credential.Sid, app.AccountSid, StringComparison.OrdinalIgnoreCase));

            if (!found)
            {
                var fallbackEntry = new CredentialEntry { Sid = app.AccountSid };
                var fallbackItem = new CredentialDisplayItem(fallbackEntry, sidResolver, sidNames);
                accountComboBox.Items.Add(fallbackItem);
                accountComboBox.SelectedItem = fallbackItem;
            }
        }
    }

    /// <summary>
    /// Selects the config combo item and sets ACL path state for an existing app entry.
    /// </summary>
    public void SelectConfigAndAclPath(
        AppEntry app,
        IAppConfigService appConfigService,
        AclConfigSection aclSection,
        ComboBox configComboBox,
        bool hasLoadedConfigs)
    {
        // SetExePath rebuilds the folder depth combo + updates ACL state;
        // SelectFolderDepth must come after to override the default depth.
        aclSection.SetExePath(app.ExePath, app.IsFolder);
        aclSection.SelectFolderDepth(app.FolderAclDepth);

        if (hasLoadedConfigs)
        {
            var currentPath = appConfigService.GetConfigPath(app.Id);
            configComboBox.SelectedIndex = 0;
            if (currentPath != null)
                AppEditDialogPopulator.SelectComboItem<ConfigComboItem>(configComboBox,
                    item => string.Equals(item.Path, currentPath, StringComparison.OrdinalIgnoreCase),
                    startIndex: 1);
        }
    }

    /// <summary>
    /// Validates dialog state and builds the result AppEntry.
    /// Returns null and sets <paramref name="state"/>.StatusText on validation failure.
    /// </summary>
    public AppEntry? ValidateAndBuild(
        IAppEditDialogState state,
        AclConfigSection aclSection,
        IpcCallerSection ipcSection,
        EnvVarsSection envVarsSection,
        List<AppEntry> existingApps,
        AppEntry? existing,
        string? preGeneratedId)
    {
        state.StatusText = "";

        var filePath = state.FilePathText.Trim();
        var isFolder = state.IsFolder;
        if (existing == null && !PathHelper.IsUrlScheme(filePath) && !isFolder
            && Directory.Exists(filePath) && !File.Exists(filePath))
        {
            isFolder = true;
        }

        var selectedAccount = (state.SelectedAccountItem as CredentialDisplayItem)?.Credential;
        var selectedContainer = state.SelectedAccountItem as AppContainerDisplayItem;

        var error = entryBuilder.Validate(state.NameText, filePath, isFolder,
            selectedAccount, state.ManageShortcuts, existingApps, existing?.Id,
            appContainerName: selectedContainer?.Container.Name);
        if (error != null)
        {
            state.StatusText = error;
            return null;
        }

        var aclError = aclSection.Validate(filePath, isFolder);
        if (aclError != null)
        {
            state.StatusText = aclError;
            return null;
        }

        var aclResult = aclSection.BuildResult(filePath, isFolder);

        var duplicateEnvVar = envVarsSection.GetFirstDuplicateName();
        if (duplicateEnvVar != null)
        {
            state.StatusText = $"Duplicate environment variable name: {duplicateEnvVar}";
            return null;
        }

        List<string>? ipcCallers = state.OverrideIpcCallers
            ? ipcSection.GetCallers()
            : null;

        var accountSid = selectedContainer != null ? "" : selectedAccount!.Sid;
        var appContainerName = selectedContainer?.Container.Name;

        // For container apps, preserve the original PrivilegeLevel value saved before container
        // selection forced it; actual setting is irrelevant for containers but preserved in case
        // the user later switches back to a user account in a future edit.
        PrivilegeLevel? privilegeLevel = selectedContainer != null
            ? _switchHandler.PriorPrivilegeLevel
            : state.SelectedPrivilegeLevel;

        var argsTemplate = state.ArgumentsTemplateText;
        return entryBuilder.Build(new AppEntryBuildOptions(
            Name: state.NameText,
            ExePath: filePath,
            IsFolder: isFolder,
            AccountSid: accountSid,
            ManageShortcuts: state.ManageShortcuts,
            DefaultArgs: state.DefaultArgsText,
            AllowPassArgs: state.AllowPassArgs,
            WorkingDirectory: state.WorkingDirText,
            AllowPassWorkingDir: state.AllowPassWorkDir,
            IpcCallers: ipcCallers,
            RestrictAcl: aclResult.RestrictAcl,
            AclMode: aclResult.AclMode,
            AclTarget: aclResult.AclTarget,
            FolderAclDepth: aclResult.Depth,
            DeniedRights: aclResult.DeniedRights,
            AllowedAclEntries: aclResult.AllowedEntries,
            ExistingId: existing?.Id,
            LastKnownExeTimestamp: existing?.LastKnownExeTimestamp,
            PreGeneratedId: preGeneratedId,
            PrivilegeLevel: privilegeLevel,
            AppContainerName: appContainerName,
            EnvironmentVariables: envVarsSection.GetItems(),
            ArgumentsTemplate: string.IsNullOrEmpty(argsTemplate) ? null : argsTemplate,
            ExistingApps: existingApps));
    }
}

public record PopulateFromExistingResult(
    bool OverrideIpcCallers,
    PrivilegeLevel? SelectedPrivilegeLevel);