using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Security;

public interface IPinService
{
    bool VerifyPin(ProtectedString pin, CredentialStore store);
    PinVerificationResult VerifyPinForSession(ProtectedString pin, CredentialStore store);
    bool VerifyDerivedKey(ReadOnlySpan<byte> pinDerivedKey, CredentialStore store);
    PinKeyRotationResult ChangePin(ISecureSecretSnapshotSource oldPinDerivedKey, ProtectedString newPin, CredentialStore store);
    PinResetResult ResetPin(ProtectedString newPin);
    SecureSecret DeriveKeySecret(ProtectedString pin, byte[] salt);
}
