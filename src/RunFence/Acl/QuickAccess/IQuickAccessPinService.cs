namespace RunFence.Acl.QuickAccess;

public interface IQuickAccessPinService
{
    /// <summary>
    /// Pin folder paths for an account. Skipped silently if account has no credentials.
    /// Must be called on the UI thread (PinDerivedKey.Unprotect requirement).
    /// </summary>
    void PinFolders(string accountSid, IReadOnlyList<string> paths);

    /// <summary>
    /// Unpin folder paths for an account. Must be called on the UI thread.
    /// </summary>
    void UnpinFolders(string accountSid, IReadOnlyList<string> paths);

    /// <summary>
    /// Pin all non-traverse Allow folder grants for all eligible accounts.
    /// Used after loading an additional config. Must be called on the UI thread.
    /// </summary>
    void PinAllGrantedFolders();
}
