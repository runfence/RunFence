using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Startup.UI;

/// <summary>
/// Owns recovery PIN output until the startup loader transfers or disposes it.
/// </summary>
public sealed class RecoveryPinOutcome : IDisposable
{
    private SecureSecret? _key;
    private SecureSecret? _mismatchKey;

    public RecoveryPinOutcome(CredentialStore store, SecureSecret key, SecureSecret? mismatchKey)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
        _key = key ?? throw new ArgumentNullException(nameof(key));
        _mismatchKey = mismatchKey;
    }

    public CredentialStore Store { get; }

    public SecureSecret TakeKey()
    {
        if (_key is null)
            throw new InvalidOperationException("The recovery PIN-derived key has already been taken.");

        var key = _key;
        _key = null;
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
        _key?.Dispose();
        _key = null;
        _mismatchKey?.Dispose();
        _mismatchKey = null;
    }
}
