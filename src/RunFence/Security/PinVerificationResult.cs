using RunFence.Core;

namespace RunFence.Security;

/// <summary>
/// Owns a verified session PIN-derived key until the caller transfers or disposes it.
/// </summary>
public sealed class PinVerificationResult : IDisposable
{
    private SecureSecret? _pinDerivedKey;

    private PinVerificationResult(bool succeeded, SecureSecret? pinDerivedKey)
    {
        Succeeded = succeeded;
        _pinDerivedKey = pinDerivedKey;
    }

    public bool Succeeded { get; }

    public static PinVerificationResult Success(SecureSecret key)
        => new(true, key ?? throw new ArgumentNullException(nameof(key)));

    public static PinVerificationResult Failed()
        => new(false, null);

    public SecureSecret TakePinDerivedKey()
    {
        if (!Succeeded)
            throw new InvalidOperationException("A failed PIN verification result has no key to take.");

        if (_pinDerivedKey is null)
            throw new InvalidOperationException("The verified PIN-derived key has already been taken.");

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
