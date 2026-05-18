using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Startup;

/// <summary>
/// Owns startup credential-load secrets until they are transferred into the session
/// or disposed on an error path.
/// </summary>
public sealed class CredentialLoadResult : IDisposable
{
    private SecureSecret? _pinDerivedKey;
    private SecureSecret? _mismatchKey;

    public CredentialLoadResult(
        CredentialStore store,
        SecureSecret pinDerivedKey,
        SecureSecret? mismatchKey,
        bool pinBypassed = false)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
        _pinDerivedKey = pinDerivedKey ?? throw new ArgumentNullException(nameof(pinDerivedKey));
        _mismatchKey = mismatchKey;
        PinBypassed = pinBypassed;
    }

    public CredentialStore Store { get; }

    public bool PinBypassed { get; }

    public SecureSecret TakePinDerivedKey()
    {
        if (_pinDerivedKey is null)
            throw new InvalidOperationException("The startup PIN-derived key has already been taken.");

        var key = _pinDerivedKey;
        _pinDerivedKey = null;
        return key;
    }

    public SecureSecret? TakeMismatchKey()
    {
        var key = _mismatchKey;
        _mismatchKey = null;
        return key;
    }

    public void Dispose()
    {
        _pinDerivedKey?.Dispose();
        _pinDerivedKey = null;
        _mismatchKey?.Dispose();
        _mismatchKey = null;
    }
}
