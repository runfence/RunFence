using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launching.Resolution;

namespace RunFence.Apps.UI;

/// <summary>
/// Handles AppEditDialog validation/build logic.
/// </summary>
public class AppEditDialogController
{
    private readonly AppEntryBuilder _entryBuilder;
    private readonly IExecutablePathResolver _executablePathResolver;
    private readonly AppEditDialogInputValidator _inputValidator;
    private readonly AppEditDialogAclConfigBuilder _aclConfigBuilder;
    private AppEditAccountSwitchHandler _switchHandler = null!;

    public AppEditDialogController(
        AppEntryBuilder entryBuilder,
        IExecutablePathResolver executablePathResolver,
        AppEditDialogInputValidator inputValidator,
        AppEditDialogAclConfigBuilder aclConfigBuilder)
    {
        _entryBuilder = entryBuilder;
        _executablePathResolver = executablePathResolver;
        _inputValidator = inputValidator;
        _aclConfigBuilder = aclConfigBuilder;
    }

    /// <summary>
    /// Binds the controller to the per-dialog switch handler. Must be called before any operations.
    /// </summary>
    public void Initialize(AppEditAccountSwitchHandler switchHandler)
    {
        _switchHandler = switchHandler;
    }

    /// <summary>
    /// Validates a captured dialog snapshot and builds the result AppEntry.
    /// </summary>
    public AppEditDialogBuildResult ValidateAndBuild(AppEditDialogInputSnapshot snapshot)
    {
        _inputValidator.EnsureConsistent(snapshot);

        var filePath = snapshot.FilePathText.Trim();
        var isFolder = snapshot.IsFolder;
        if (snapshot.ExistingApp == null && !PathHelper.IsUrlScheme(filePath) && !isFolder
            && Directory.Exists(filePath) && !File.Exists(filePath))
        {
            isFolder = true;
        }

        if (!isFolder && !PathHelper.IsUrlScheme(filePath))
        {
            filePath = _executablePathResolver.TryResolvePath(
                filePath,
                ExecutablePathResolutionContext.CurrentProcess()) ?? filePath;
        }

        var selectedAccount = snapshot.SelectedAccountSid != null
            ? new CredentialEntry { Sid = snapshot.SelectedAccountSid }
            : null;

        var validationError = _entryBuilder.Validate(
            snapshot.NameText,
            filePath,
            isFolder,
            selectedAccount,
            snapshot.ManageShortcuts,
            snapshot.ExistingApps.ToList(),
            snapshot.ExistingApp?.Id,
            appContainerName: snapshot.SelectedAppContainerName);
        if (validationError != null)
            return new AppEditDialogBuildResult(null, validationError);

        var aclBuildResult = _aclConfigBuilder.Build(snapshot, filePath, isFolder);
        if (aclBuildResult.ValidationError != null)
            return new AppEditDialogBuildResult(null, aclBuildResult.ValidationError);

        var inputError = _inputValidator.Validate(snapshot);
        if (inputError != null)
            return new AppEditDialogBuildResult(null, inputError);

        var aclResult = aclBuildResult.Result!.Value;
        var statusText = !isFolder && !PathHelper.IsUrlScheme(filePath) && !File.Exists(filePath)
            ? "Warning: file not found."
            : null;

        List<string>? ipcCallers = snapshot.OverrideIpcCallers ? snapshot.IpcCallers : null;
        var accountSid = snapshot.SelectedAppContainerName != null ? "" : selectedAccount!.Sid;
        var privilegeLevel = snapshot.SelectedAppContainerName != null
            ? _switchHandler.PriorPrivilegeLevel
            : snapshot.SelectedPrivilegeLevel;

        return new AppEditDialogBuildResult(
            _entryBuilder.Build(new AppEntryBuildOptions(
                Name: snapshot.NameText,
                ExePath: filePath,
                IsFolder: isFolder,
                AccountSid: accountSid,
                ManageShortcuts: snapshot.ManageShortcuts,
                DefaultArgs: snapshot.DefaultArgsText,
                AllowPassArgs: snapshot.AllowPassArgs,
                WorkingDirectory: snapshot.WorkingDirText,
                AllowPassWorkingDir: snapshot.AllowPassWorkDir,
                IpcCallers: ipcCallers,
                RestrictAcl: aclResult.RestrictAcl,
                AclMode: aclResult.AclMode,
                AclTarget: aclResult.AclTarget,
                FolderAclDepth: aclResult.Depth,
                DeniedRights: aclResult.DeniedRights,
                AllowedAclEntries: aclResult.AllowedEntries,
                ExistingId: snapshot.ExistingApp?.Id,
                LastKnownExeTimestamp: snapshot.ExistingApp?.LastKnownExeTimestamp,
                PreGeneratedId: snapshot.PreGeneratedId,
                PrivilegeLevel: privilegeLevel,
                AppContainerName: snapshot.SelectedAppContainerName,
                EnvironmentVariables: snapshot.EnvironmentVariables,
                ArgumentsTemplate: snapshot.ArgumentsTemplateText,
                PathPrefixes: snapshot.AppPathPrefixes?.ToList(),
                ExistingApps: snapshot.ExistingApps.ToList())),
            statusText);
    }
}
