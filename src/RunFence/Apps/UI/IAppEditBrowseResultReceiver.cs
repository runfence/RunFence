using RunFence.Core.Models;

namespace RunFence.Apps.UI;

/// <summary>
/// Callback interface implemented by <see cref="Forms.AppEditDialog"/> so that
/// <see cref="AppEditBrowseHelper"/> can apply browse results to the dialog without
/// holding a direct reference to it.
/// </summary>
public interface IAppEditBrowseResultReceiver
{
    string GetAppName();
    void SetFilePath(string path);
    void SetAppName(string name);
    void SetFolderMode(bool isFolder);
    void SetWorkingDir(string path);
    void SetDefaultArgs(string args);
    string? GetSelectedAccountSid();

    /// <summary>
    /// Returns true when the privilege level combo is enabled and not already at
    /// Highest Allowed, High Integrity, or Basic.
    /// </summary>
    bool CanSuggestBasicPrivilegeLevel();
    void SetPrivilegeLevel(PrivilegeLevel? level);
}
