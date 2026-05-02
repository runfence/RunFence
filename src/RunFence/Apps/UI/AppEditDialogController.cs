using RunFence.Account.UI;
using RunFence.Account.UI.AppContainer;
using RunFence.Acl.UI.Forms;
using RunFence.Apps.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launching.Resolution;
using RunFence.UI.Forms;

namespace RunFence.Apps.UI;

/// <summary>
/// Handles AppEditDialog validation/build logic.
/// </summary>
public class AppEditDialogController
{
    private readonly AppEntryBuilder _entryBuilder;
    private readonly IExecutablePathResolver _executablePathResolver;
    private AppEditAccountSwitchHandler _switchHandler = null!;

    internal AppEditDialogController(
        AppEntryBuilder entryBuilder,
        IExecutablePathResolver executablePathResolver)
    {
        _entryBuilder = entryBuilder;
        _executablePathResolver = executablePathResolver;
    }

    /// <summary>
    /// Binds the controller to the per-dialog switch handler. Must be called before any operations.
    /// </summary>
    public void Initialize(AppEditAccountSwitchHandler switchHandler)
    {
        _switchHandler = switchHandler;
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

        if (!isFolder && !PathHelper.IsUrlScheme(filePath))
            filePath = _executablePathResolver.TryResolvePath(
                filePath,
                ExecutablePathResolutionContext.CurrentProcess()) ?? filePath;

        var selectedAccount = (state.SelectedAccountItem as CredentialDisplayItem)?.Credential;
        var selectedContainer = state.SelectedAccountItem as AppContainerDisplayItem;

        var error = _entryBuilder.Validate(state.NameText, filePath, isFolder,
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

        if (!isFolder && !PathHelper.IsUrlScheme(filePath) && !File.Exists(filePath))
            state.StatusText = "Warning: file not found.";

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
        return _entryBuilder.Build(new AppEntryBuildOptions(
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
            PathPrefixes: state.AppPathPrefixes?.ToList(),
            ExistingApps: existingApps));
    }
}
