namespace RunFence.Launch.Tokens;

/// <summary>
/// Acquires the elevated linked token for a UAC-filtered interactive logon token.
/// </summary>
public interface IElevatedLinkedTokenProvider
{
    /// <summary>
    /// Acquires the elevated linked token for <paramref name="hFilteredToken"/>.
    /// The returned handle is owned by the caller and must be closed via CloseHandle.
    /// </summary>
    IntPtr AcquireElevatedLinkedToken(IntPtr hFilteredToken);
}
