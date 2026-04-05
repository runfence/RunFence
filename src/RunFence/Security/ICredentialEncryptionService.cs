using System.Security;

namespace RunFence.Security;

public interface ICredentialEncryptionService
{
    byte[] Encrypt(SecureString password, byte[] pinDerivedKey);
    SecureString Decrypt(byte[] encryptedPassword, byte[] pinDerivedKey);
}