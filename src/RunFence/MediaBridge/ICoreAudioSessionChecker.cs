namespace RunFence.MediaBridge;

/// <summary>
/// Checks Core Audio sessions to determine whether any audio session
/// owned by a given user SID is currently active (playing).
/// </summary>
public interface ICoreAudioSessionChecker
{
    /// <summary>
    /// Returns true if any audio session owned by <paramref name="interactiveSid"/>
    /// is currently in the Active state on the default audio render endpoint.
    /// Returns false on any COM failure (device unavailable, no sessions, etc.).
    /// </summary>
    bool IsAnySessionActive(string interactiveSid);
}