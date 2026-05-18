using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Security;

/// <summary>
/// Owns the reset credential store and replacement PIN-derived key until transfer.
/// </summary>
public sealed class PinResetResult : IDisposable
{
    private SecureSecret? _pinDerivedKey;

    public PinResetResult(CredentialStore store, SecureSecret pinDerivedKey)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
        _pinDerivedKey = pinDerivedKey ?? throw new ArgumentNullException(nameof(pinDerivedKey));
    }

    public CredentialStore Store { get; }

    public SecureSecret TakePinDerivedKey()
    {
        if (_pinDerivedKey is null)
            throw new InvalidOperationException("The reset PIN-derived key has already been taken.");

        var key = _pinDerivedKey;
        _pinDerivedKey = null;
        return key;
    }

    public void Dispose()
    {
        _pinDerivedKey?.Dispose();
        _pinDerivedKey = null;
    }
}
