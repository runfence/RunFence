namespace RunFence.Launch.Tokens;

/// <summary>
/// Provides access to the explorer.exe token for the current session.
/// </summary>
public interface IExplorerTokenProvider
{
    /// <summary>
    /// Returns an open token handle from explorer.exe owned by the interactive user.
    /// SID-verified. Throws if no matching explorer.exe is found.
    /// Caller owns the returned handle and must close it.
    /// </summary>
    IntPtr GetExplorerToken();

    /// <summary>
    /// Returns an open token handle from explorer.exe owned by the interactive user,
    /// or <see cref="IntPtr.Zero"/> if no matching explorer.exe is found.
    /// SID-verified. Caller owns the returned handle and must close it.
    /// </summary>
    IntPtr TryGetExplorerToken();

    /// <summary>
    /// Returns an open token handle from any explorer.exe in the current session,
    /// without SID verification. Used for AppContainer launches which need the
    /// interactive user's token regardless of whether the interactive user differs
    /// from the current (admin) user.
    /// Caller owns the returned handle and must close it.
    /// </summary>
    IntPtr GetSessionExplorerToken();
}
