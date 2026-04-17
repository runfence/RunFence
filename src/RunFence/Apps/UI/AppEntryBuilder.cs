using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch;

namespace RunFence.Apps.UI;

/// <summary>
/// Validates inputs and constructs an AppEntry from dialog state.
/// </summary>
public class AppEntryBuilder(IAppEntryIdGenerator idGenerator)
{
    /// <summary>
    /// Validates all inputs for creating/editing an AppEntry.
    /// Returns null if valid, or an error message string if invalid.
    /// Pass non-null <paramref name="appContainerName"/> when an AppContainer is selected
    /// (skips the account requirement check).
    /// </summary>
    public string? Validate(string name, string exePath, bool isFolder,
        CredentialEntry? selectedAccount, bool manageShortcuts,
        List<AppEntry> existingApps, string? currentId,
        string? appContainerName = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Name is required.";

        if (string.IsNullOrWhiteSpace(exePath))
            return "File path or URL is required.";

        var isUrl = PathHelper.IsUrlScheme(exePath);

        if (isUrl)
        {
            if (appContainerName != null)
                return "URL scheme apps are not supported with AppContainers.";
            if (!ProcessLaunchHelper.ValidateUrlScheme(exePath, out var urlError))
                return urlError;
        }
        else if (isFolder)
        {
            if (!Directory.Exists(exePath))
                return "Folder not found.";
        }
        else if (!File.Exists(exePath))
        {
            return "File not found.";
        }

        if (selectedAccount == null && appContainerName == null)
            return "Please select an account or AppContainer.";

        if (manageShortcuts)
        {
            var conflict = existingApps.FirstOrDefault(a =>
                a.Id != (currentId ?? "") &&
                a.ManageShortcuts &&
                string.Equals(a.ExePath, exePath, StringComparison.OrdinalIgnoreCase));

            if (conflict != null)
                return $"Shortcuts already managed by '{conflict.Name}' for this path.";
        }

        return null;
    }

    /// <summary>
    /// Builds an AppEntry from the validated dialog state.
    /// Pass non-null <see cref="AppEntryBuildOptions.AppContainerName"/> for AppContainer apps (sets AccountSid = "").
    /// </summary>
    public AppEntry Build(AppEntryBuildOptions opts)
    {
        var isUrl = PathHelper.IsUrlScheme(opts.ExePath);
        var resolvedWorkDir = string.IsNullOrWhiteSpace(opts.WorkingDirectory) ? null : opts.WorkingDirectory.Trim();

        return new AppEntry
        {
            Id = opts.ExistingId ?? opts.PreGeneratedId ?? idGenerator.GenerateUniqueId((opts.ExistingApps ?? []).Select(a => a.Id)),
            Name = opts.Name.Trim(),
            ExePath = opts.ExePath,
            IsUrlScheme = isUrl,
            IsFolder = opts.IsFolder,
            DefaultArguments = opts.IsFolder ? "" : opts.DefaultArgs,
            AllowPassingArguments = !opts.IsFolder && opts.AllowPassArgs,
            WorkingDirectory = opts.IsFolder || isUrl ? null : resolvedWorkDir,
            AllowPassingWorkingDirectory = !opts.IsFolder && !isUrl && opts.AllowPassWorkingDir,
            PrivilegeLevel = opts.PrivilegeLevel,
            AccountSid = opts.AccountSid,
            AppContainerName = opts.AppContainerName,
            RestrictAcl = opts.RestrictAcl,
            AclMode = opts.AclMode,
            DeniedRights = opts.DeniedRights,
            AllowedAclEntries = opts.AllowedAclEntries,
            AclTarget = opts.AclTarget,
            FolderAclDepth = opts.FolderAclDepth,
            ManageShortcuts = opts.ManageShortcuts,
            LastKnownExeTimestamp = opts.IsFolder ? null : opts.LastKnownExeTimestamp,
            AllowedIpcCallers = opts.IpcCallers,
            EnvironmentVariables = opts.IsFolder || isUrl || opts.EnvironmentVariables?.Count is null or 0
                ? null
                : opts.EnvironmentVariables,
            ArgumentsTemplate = opts.IsFolder ? null : string.IsNullOrEmpty(opts.ArgumentsTemplate) ? null : opts.ArgumentsTemplate
        };
    }

}