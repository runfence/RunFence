using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Security;

/// <summary>
/// Owns the rotated credential store and replacement PIN-derived key until transfer.
/// </summary>
public sealed class PinKeyRotationResult : IDisposable
{
    private SecureSecret? _newPinDerivedKey;

    public PinKeyRotationResult(CredentialStore store, SecureSecret newPinDerivedKey)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
        _newPinDerivedKey = newPinDerivedKey ?? throw new ArgumentNullException(nameof(newPinDerivedKey));
    }

    public CredentialStore Store { get; }

    public SecureSecret TakeNewPinDerivedKey()
    {
        if (_newPinDerivedKey is null)
            throw new InvalidOperationException("The rotated PIN-derived key has already been taken.");

        var key = _newPinDerivedKey;
        _newPinDerivedKey = null;
        return key;
    }

    public void Dispose()
    {
        _newPinDerivedKey?.Dispose();
        _newPinDerivedKey = null;
    }
}
