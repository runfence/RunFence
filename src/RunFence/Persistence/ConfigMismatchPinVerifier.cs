using System.Security.Cryptography;
using RunFence.Core;
using RunFence.Security;

namespace RunFence.Persistence;

public class ConfigMismatchPinVerifier(IPinService pinService)
{
    public ConfigMismatchPinVerificationResult VerifyTemporary(
        ProtectedString pin,
        byte[] fileSalt,
        Action<ISecureSecretSnapshotSource> verify)
        => Verify(pin, fileSalt, verify, returnVerifiedKey: false);

    public ConfigMismatchPinVerificationResult VerifyAndReturnKey(
        ProtectedString pin,
        byte[] fileSalt,
        Action<ISecureSecretSnapshotSource> verify)
        => Verify(pin, fileSalt, verify, returnVerifiedKey: true);

    private ConfigMismatchPinVerificationResult Verify(
        ProtectedString pin,
        byte[] fileSalt,
        Action<ISecureSecretSnapshotSource> verify,
        bool returnVerifiedKey)
    {
        ArgumentNullException.ThrowIfNull(pin);
        ArgumentNullException.ThrowIfNull(fileSalt);
        ArgumentNullException.ThrowIfNull(verify);

        SecureSecret? candidate = null;
        try
        {
            candidate = pinService.DeriveKeySecret(pin, fileSalt);
        }
        catch (Exception ex)
        {
            return ConfigMismatchPinVerificationResult.AbortToRecovery(ex);
        }

        try
        {
            verify(candidate);

            if (!returnVerifiedKey)
                return ConfigMismatchPinVerificationResult.VerifiedTemporaryOnly();

            var verifiedKey = candidate;
            candidate = null;
            return ConfigMismatchPinVerificationResult.VerifiedWithReturnedKey(verifiedKey);
        }
        catch (CryptographicException)
        {
            return ConfigMismatchPinVerificationResult.WrongPin();
        }
        catch (Exception ex)
        {
            return ConfigMismatchPinVerificationResult.AbortToRecovery(ex);
        }
        finally
        {
            candidate?.Dispose();
        }
    }
}
