using System.Security.Principal;

namespace RunFence.Core;

/// <summary>
/// Manages interactive user SID state and current user SID caching.
/// Stateless name resolution utilities have been moved to <see cref="SidNameResolver"/>.
/// </summary>
/// <remarks>
/// <c>_interactiveUserSid</c> and <c>_interactiveUserIsSameAsCurrent</c> are each individually
/// volatile, but there is no multi-field atomicity guarantee: a reader may observe the new SID
/// with the old <c>_interactiveUserIsSameAsCurrent</c> flag (or vice versa) during the brief
/// window of <see cref="InitializeInteractiveUserSid"/>. This is intentional; a brief
/// inconsistency during a fast user switch is acceptable — the next read will be consistent.
/// </remarks>
public static class SidResolutionHelper
{
    private static readonly string CurrentUserSid = WindowsIdentity.GetCurrent().User!.Value;
    private static volatile string? _interactiveUserSid;
    private static volatile bool _interactiveUserIsSameAsCurrent;

    /// <summary>Returns the current user's SID string. Cached at first call.</summary>
    public static string GetCurrentUserSid() => CurrentUserSid;

    /// <summary>
    /// Returns the interactive user's SID, or null if unavailable (explorer not running).
    /// May equal <see cref="GetCurrentUserSid"/> when the interactive user is the same account.
    /// Use <see cref="IsCurrentUserInteractive"/> to check if they are the same.
    /// </summary>
    public static string? GetInteractiveUserSid() => _interactiveUserSid;

    /// <summary>
    /// Returns true if the given SID matches the interactive user (and is not the current user).
    /// Safe to call with null — returns false.
    /// </summary>
    public static bool IsInteractiveUserSid(string? sid)
        => sid != null && _interactiveUserSid != null &&
           string.Equals(sid, _interactiveUserSid, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true if the given SID is the Windows SYSTEM account (S-1-5-18).
    /// Safe to call with null — returns false.
    /// </summary>
    public static bool IsSystemSid(string? sid)
        => string.Equals(sid, SidConstants.SystemSid, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true if the given SID can launch apps without a stored password:
    /// the current account, the interactive user (explorer token), or the SYSTEM account.
    /// </summary>
    public static bool CanLaunchWithoutPassword(string? sid)
        => string.Equals(sid, CurrentUserSid, StringComparison.OrdinalIgnoreCase)
           || IsInteractiveUserSid(sid)
           || IsSystemSid(sid);

    /// <summary>
    /// Returns true if RunFence must use Job Object tracking to distinguish its own launched processes
    /// from unrelated processes running under the same SID (SYSTEM and interactive user both have many
    /// independent processes that are not RunFence-owned).
    /// Safe to call with null — returns false.
    /// </summary>
    public static bool NeedsProcessJobTracking(string? sid) => IsSystemSid(sid) || IsInteractiveUserSid(sid);

    /// <summary>
    /// Returns true when the interactive desktop user is the same account as the current elevated process.
    /// This indicates that UAC elevation provides no security boundary for this account.
    /// Returns false if the interactive user could not be detected or is a different account.
    /// </summary>
    public static bool IsCurrentUserInteractive() => _interactiveUserIsSameAsCurrent;

    /// <summary>
    /// Initializes the interactive user SID from explorer.exe. Call once at startup.
    /// Always stores the actual SID; use <see cref="IsCurrentUserInteractive"/> to check if
    /// the interactive user is the same account as the current elevated process.
    /// </summary>
    public static void InitializeInteractiveUserSid()
    {
        var sid = NativeTokenHelper.TryGetInteractiveUserSid()?.Value;
        _interactiveUserSid = sid;
        _interactiveUserIsSameAsCurrent = sid != null &&
            string.Equals(sid, CurrentUserSid, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Re-runs the interactive user SID initialization. Call when a fast user switch or
    /// session change event is detected (<see cref="Microsoft.Win32.SystemEvents.SessionSwitch"/>)
    /// so that the cached SID reflects the current interactive desktop user.
    /// </summary>
    public static void ReinitializeInteractiveUserSid() => InitializeInteractiveUserSid();
}