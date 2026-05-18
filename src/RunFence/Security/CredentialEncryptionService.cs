using System.Security.Cryptography;
using RunFence.Core;

namespace RunFence.Security;

public class CredentialEncryptionService(IDpapiProtector dpapiProtector)
    : ICredentialEncryptionSpanService
{
    public byte[] Encrypt(ProtectedString password, ReadOnlySpan<byte> pinDerivedKey)
    {
        var entropy = HkdfKeyDerivation.DeriveDpapiEntropy(pinDerivedKey);
        try
        {
            return password.UseUtf16BytesSnapshot(passwordBytes => dpapiProtector.Protect(passwordBytes, entropy));
        }
        finally
        {
            Array.Clear(entropy, 0, entropy.Length);
        }
    }

    public ProtectedString Decrypt(byte[] encryptedPassword, ReadOnlySpan<byte> pinDerivedKey)
    {
        var entropy = HkdfKeyDerivation.DeriveDpapiEntropy(pinDerivedKey);
        try
        {
            return dpapiProtector.UnprotectToProtectedString(encryptedPassword, entropy);
        }
        finally
        {
            Array.Clear(entropy, 0, entropy.Length);
        }
    }
}
