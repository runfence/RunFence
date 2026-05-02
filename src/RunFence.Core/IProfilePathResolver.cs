namespace RunFence.Core;

/// <summary>
/// Resolves profile-related filesystem paths for a given SID.
/// All methods read from the registry (ProfileList) or the file system — they do NOT contact AD.
/// </summary>
public interface IProfilePathResolver
{
    /// <summary>Looks up the profile path for a SID from the registry ProfileList. Returns null if not found.</summary>
    string? TryGetProfilePath(string sid);

    /// <summary>
    /// Resolves a SID string to a username by looking up the profile path from the registry
    /// and extracting the leaf folder name. Returns null if the SID has no registered profile.
    /// </summary>
    string? TryResolveNameFromRegistry(string sid);

    /// <summary>
    /// Returns the Desktop path for the given SID.
    /// Returns null for non-current accounts whose profile is not registered.
    /// </summary>
    string? TryGetDesktopPath(string sid, bool isCurrentAccount);

    /// <summary>
    /// Returns the per-user Start Menu Programs path for the given SID.
    /// Returns null for non-current accounts whose profile is not registered.
    /// </summary>
    string? TryGetStartMenuProgramsPath(string sid, bool isCurrentAccount);

    /// <summary>
    /// Returns the taskbar pinned shortcuts folder path for the given SID.
    /// Returns null for non-current accounts whose profile is not registered.
    /// </summary>
    string? TryGetTaskBarPath(string sid, bool isCurrentAccount);
}
