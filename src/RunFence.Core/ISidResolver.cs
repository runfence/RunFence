using RunFence.Core.Models;

namespace RunFence.Core;

/// <summary>
/// Injectable abstraction for OS-dependent SID resolution operations.
/// Allows mocking in tests without hitting the Windows identity subsystem.
/// </summary>
public interface ISidResolver
{
    /// <summary>Resolves an account name (e.g. "DOMAIN\user") to a SID string. Returns null on failure.</summary>
    string? TryResolveSid(string accountName);

    /// <summary>Resolves a SID string to a human-readable account name. Returns null on failure.</summary>
    string? TryResolveName(string sidString);

    /// <summary>
    /// Resolves a SID string to a username by looking up the profile path from the registry
    /// and extracting the leaf folder name. Returns null if the SID has no registered profile.
    /// </summary>
    string? TryResolveNameFromRegistry(string sid);

    /// <summary>Looks up the profile path for a SID from the registry ProfileList. Returns null if not found.</summary>
    string? TryGetProfilePath(string sid);

    /// <summary>Returns the current user's SID string.</summary>
    string GetCurrentUserSid();

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

    /// <summary>
    /// Resolves an account name to a SID, checking local users first for unambiguous
    /// resolution, then falling back to NTAccount for explicit domain prefixes.
    /// Returns null if resolution fails.
    /// </summary>
    string? ResolveSidFromName(string accountName, List<LocalUserAccount>? localUsers);
}