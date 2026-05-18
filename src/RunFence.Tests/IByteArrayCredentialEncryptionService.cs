using RunFence.Core;

namespace RunFence.Tests;

public interface IByteArrayCredentialEncryptionService
{
    byte[] Encrypt(ProtectedString password, byte[] pinDerivedKey);
    ProtectedString Decrypt(byte[] encryptedPassword, byte[] pinDerivedKey);
}
