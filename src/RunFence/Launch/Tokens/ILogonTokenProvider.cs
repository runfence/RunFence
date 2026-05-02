using RunFence.Core;

namespace RunFence.Launch.Tokens;

/// <summary>
/// Acquires a Windows logon token for a given credential set or token source.
/// </summary>
public interface ILogonTokenProvider
{
    /// <summary>
    /// Acquires a logon token for the given credentials, or opens the current process token
    /// when <paramref name="password"/> is null (current-account launch).
    /// </summary>
    IntPtr AcquireLogonToken(ProtectedString? password, string? domain,
        string? username, LaunchTokenSource tokenSource = LaunchTokenSource.Credentials);
}
