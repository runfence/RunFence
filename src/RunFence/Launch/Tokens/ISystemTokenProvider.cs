namespace RunFence.Launch.Tokens;

public interface ISystemTokenProvider
{
    /// <summary>
    /// Acquires a primary SYSTEM token from winlogon.exe in the current session.
    /// The returned handle is owned by the caller and must be closed via CloseHandle.
    /// </summary>
    IntPtr AcquireSystemToken();
}
