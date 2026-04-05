using RunFence.Core.Models;

namespace RunFence.Security;

public interface IPinService
{
    bool VerifyPin(string pin, CredentialStore store, out byte[] pinDerivedKey);
    (CredentialStore store, byte[] newPinDerivedKey) ChangePin(byte[] oldPinDerivedKey, string newPin, CredentialStore store);
    (CredentialStore store, byte[] pinDerivedKey) ResetPin(string newPin);
    byte[] DeriveKey(string pin, byte[] salt);
}