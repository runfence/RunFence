using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Startup.UI;

/// <summary>
/// Owns startup verify-dialog output until the startup loader transfers or disposes it.
/// </summary>
public sealed class PinVerifyOutcome : IDisposable
{
    private SecureSecret? _key;
    private SecureSecret? _mismatchKey;

    private PinVerifyOutcome(bool canceled, CredentialStore? newStore, SecureSecret? key, SecureSecret? mismatchKey)
    {
        IsCanceled = canceled;
        NewStore = newStore;
        _key = key;
        _mismatchKey = mismatchKey;
    }

    public bool IsCanceled { get; }

    public CredentialStore? NewStore { get; }

    public static PinVerifyOutcome Canceled()
        => new(true, null, null, null);

    public static PinVerifyOutcome Verified(SecureSecret key, SecureSecret? mismatchKey)
        => new(false, null, key ?? throw new ArgumentNullException(nameof(key)), mismatchKey);

    public static PinVerifyOutcome Reset(CredentialStore newStore, SecureSecret key)
        => new(false, newStore ?? throw new ArgumentNullException(nameof(newStore)), key ?? throw new ArgumentNullException(nameof(key)), null);

    public SecureSecret TakeKey()
    {
        if (IsCanceled)
            throw new InvalidOperationException("A canceled verify outcome has no key to take.");

        if (_key is null)
            throw new InvalidOperationException("The verify outcome key has already been taken.");

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
