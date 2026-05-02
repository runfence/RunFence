using RunFence.Core.Models;

namespace RunFence.Core;

/// <summary>
/// Injectable abstraction for OS-dependent SID resolution operations.
/// Allows mocking in tests without hitting the Windows identity subsystem.
/// Profile-path related methods are in <see cref="IProfilePathResolver"/>.
/// </summary>
public interface ISidResolver
{
    /// <summary>Resolves an account name (e.g. "DOMAIN\user") to a SID string. Returns null on failure.</summary>
    string? TryResolveSid(string accountName);

    /// <summary>Resolves a SID string to a human-readable account name. Returns null on failure.</summary>
    string? TryResolveName(string sidString);

    /// <summary>Returns the current user's SID string.</summary>
    string GetCurrentUserSid();

    /// <summary>
    /// Resolves an account name to a SID, checking local users first for unambiguous
    /// resolution, then falling back to NTAccount for explicit domain prefixes.
    /// Returns null if resolution fails.
    /// </summary>
    string? ResolveSidFromName(string accountName, IReadOnlyList<LocalUserAccount>? localUsers);
}