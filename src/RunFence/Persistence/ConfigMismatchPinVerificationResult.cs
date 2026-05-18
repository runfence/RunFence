using RunFence.Core;

namespace RunFence.Persistence;

public sealed class ConfigMismatchPinVerificationResult : IDisposable
{
    private SecureSecret? _verifiedKey;

    private ConfigMismatchPinVerificationResult(
        StatusKind status,
        Exception? fatalException,
        SecureSecret? verifiedKey)
    {
        Status = status;
        FatalException = fatalException;
        _verifiedKey = verifiedKey;
    }

    public StatusKind Status { get; }

    public Exception? FatalException { get; }

    public SecureSecret TakeVerifiedKey(string alreadyTakenMessage)
    {
        if (Status != StatusKind.VerifiedWithReturnedKey)
            throw new InvalidOperationException("This verification result does not own a verified key.");

        var key = _verifiedKey;
        if (key == null)
            throw new InvalidOperationException(alreadyTakenMessage);

        _verifiedKey = null;
        return key;
    }

    public void Dispose()
    {
        _verifiedKey?.Dispose();
        _verifiedKey = null;
    }

    public static ConfigMismatchPinVerificationResult WrongPin()
        => new(StatusKind.WrongPin, fatalException: null, verifiedKey: null);

    public static ConfigMismatchPinVerificationResult VerifiedTemporaryOnly()
        => new(StatusKind.VerifiedTemporaryOnly, fatalException: null, verifiedKey: null);

    public static ConfigMismatchPinVerificationResult VerifiedWithReturnedKey(SecureSecret verifiedKey)
        => new(
            StatusKind.VerifiedWithReturnedKey,
            fatalException: null,
            verifiedKey ?? throw new ArgumentNullException(nameof(verifiedKey)));

    public static ConfigMismatchPinVerificationResult AbortToRecovery(Exception fatalException)
        => new(
            StatusKind.AbortToRecovery,
            fatalException ?? throw new ArgumentNullException(nameof(fatalException)),
            verifiedKey: null);

    public enum StatusKind
    {
        WrongPin,
        VerifiedTemporaryOnly,
        VerifiedWithReturnedKey,
        AbortToRecovery
    }
}
