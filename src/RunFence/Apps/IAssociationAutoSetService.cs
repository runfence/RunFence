namespace RunFence.Apps;

public interface IAssociationAutoSetService
{
    /// <summary>Auto-sets HKCU association overrides for all target users (interactive + credentials).</summary>
    AssociationAutoSetResult AutoSetForAllUsers();

    /// <summary>Auto-sets HKCU association overrides for a single user by SID.</summary>
    AssociationAutoSetResult AutoSetForUser(string sid);

    /// <summary>
    /// Clears the SID from the completed cache and re-runs auto-set for that user.
    /// Use when a SID is newly added as a global IPC caller mid-session (after startup's AutoSetForAllUsers already ran).
    /// </summary>
    AssociationAutoSetResult ForceAutoSetForUser(string sid);

    /// <summary>Restores original handlers for all users (for Cleanup).</summary>
    void RestoreForAllUsers();

    /// <summary>Restores original handlers for a single user (e.g. when Manage Associations is toggled off).</summary>
    void RestoreForUser(string sid);

    /// <summary>Restores original handler for a specific association key for all users.</summary>
    void RestoreKeyForAllUsers(string key);
}
