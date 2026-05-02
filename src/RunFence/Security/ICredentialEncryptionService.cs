using RunFence.Core;

namespace RunFence.Security;

public interface ICredentialEncryptionService
{
    byte[] Encrypt(ProtectedString password, byte[] pinDerivedKey);
    ProtectedString Decrypt(byte[] encryptedPassword, byte[] pinDerivedKey);
}