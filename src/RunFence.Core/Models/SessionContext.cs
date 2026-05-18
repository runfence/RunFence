namespace RunFence.Core.Models;

/// <summary>
/// Bundles runtime session state. All property reads and writes must happen on the UI thread.
/// IPC handlers access this data indirectly via Form.Invoke, which serializes on the UI thread.
/// </summary>
public class SessionContext : IDisposable
{
    private SecureSecret? _ownedPinDerivedKey;
    private ISecureSecretSnapshotSource? _pinDerivedKey;

    public AppDatabase Database { get; set; } = new();
    public CredentialStore CredentialStore { get; set; } = new();

    public ISecureSecretSnapshotSource PinDerivedKey
        => _pinDerivedKey ?? throw new InvalidOperationException("The session PIN-derived key has not been initialized.");

    public DateTime? LastPinVerifiedAt { get; set; }

    public void ReplacePinDerivedKey(SecureSecret newKey)
    {
        ArgumentNullException.ThrowIfNull(newKey);

        var oldOwnedKey = _ownedPinDerivedKey;

        _ownedPinDerivedKey = newKey;
        _pinDerivedKey = new SessionKeySnapshotSource(newKey);

        if (!ReferenceEquals(oldOwnedKey, newKey))
            oldOwnedKey?.Dispose();
    }

    public void Dispose()
    {
        _ownedPinDerivedKey?.Dispose();
        _ownedPinDerivedKey = null;
        _pinDerivedKey = null;
    }

    private sealed class SessionKeySnapshotSource(SecureSecret ownedKey) : ISecureSecretSnapshotSource
    {
        public void UseSnapshot(SecureSecretAction action) => ownedKey.UseSnapshot(action);

        public T TransformSnapshot<T>(SecureSecretFunc<T> action) => ownedKey.TransformSnapshot(action);
    }
}
