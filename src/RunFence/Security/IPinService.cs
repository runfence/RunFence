using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Security;

public interface IPinService
{
    bool VerifyPin(ProtectedString pin, CredentialStore store, out byte[] pinDerivedKey);
    bool VerifyDerivedKey(byte[] pinDerivedKey, CredentialStore store);
    (CredentialStore store, byte[] newPinDerivedKey) ChangePin(byte[] oldPinDerivedKey, ProtectedString newPin, CredentialStore store);
    (CredentialStore store, byte[] pinDerivedKey) ResetPin(ProtectedString newPin);
    byte[] DeriveKey(ProtectedString pin, byte[] salt);
}
