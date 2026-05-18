using RunFence.Core;

namespace RunFence.Security;

public interface ICredentialEncryptionSpanService
{
    byte[] Encrypt(ProtectedString password, ReadOnlySpan<byte> pinDerivedKey);
    ProtectedString Decrypt(byte[] encryptedPassword, ReadOnlySpan<byte> pinDerivedKey);
}
