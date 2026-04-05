using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using RunFence.Core;

namespace RunFence.Security;

public class CredentialEncryptionService : ICredentialEncryptionService
{
    public byte[] Encrypt(SecureString password, byte[] pinDerivedKey)
    {
        var entropy = HkdfKeyDerivation.DeriveDpapiEntropy(pinDerivedKey);
        byte[]? passwordBytes = null;
        GCHandle bytesHandle = default;
        var bstr = IntPtr.Zero;

        try
        {
            bstr = Marshal.SecureStringToBSTR(password);
            int len = Marshal.ReadInt32(bstr, -4);
            passwordBytes = new byte[len];
            bytesHandle = GCHandle.Alloc(passwordBytes, GCHandleType.Pinned);
            Marshal.Copy(bstr, passwordBytes, 0, len);

            return ProtectedData.Protect(passwordBytes, entropy, DataProtectionScope.CurrentUser);
        }
        finally
        {
            if (bstr != IntPtr.Zero)
                Marshal.ZeroFreeBSTR(bstr);
            if (passwordBytes != null)
            {
                Array.Clear(passwordBytes, 0, passwordBytes.Length);
                if (bytesHandle.IsAllocated)
                    bytesHandle.Free();
            }

            Array.Clear(entropy, 0, entropy.Length);
        }
    }

    public SecureString Decrypt(byte[] encryptedPassword, byte[] pinDerivedKey)
    {
        var entropy = HkdfKeyDerivation.DeriveDpapiEntropy(pinDerivedKey);
        byte[]? decryptedBytes = null;
        char[]? chars = null;
        GCHandle bytesHandle = default;
        GCHandle charsHandle = default;

        try
        {
            decryptedBytes = ProtectedData.Unprotect(encryptedPassword, entropy, DataProtectionScope.CurrentUser);
            bytesHandle = GCHandle.Alloc(decryptedBytes, GCHandleType.Pinned);

            // BSTR is UTF-16LE
            chars = new char[decryptedBytes.Length / 2];
            charsHandle = GCHandle.Alloc(chars, GCHandleType.Pinned);

            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = (char)(decryptedBytes[i * 2] | (decryptedBytes[i * 2 + 1] << 8));
            }

            var secureString = new SecureString();
            foreach (var c in chars)
                secureString.AppendChar(c);
            secureString.MakeReadOnly();

            return secureString;
        }
        finally
        {
            if (chars != null)
            {
                Array.Clear(chars, 0, chars.Length);
                if (charsHandle.IsAllocated)
                    charsHandle.Free();
            }

            if (decryptedBytes != null)
            {
                Array.Clear(decryptedBytes, 0, decryptedBytes.Length);
                if (bytesHandle.IsAllocated)
                    bytesHandle.Free();
            }

            Array.Clear(entropy, 0, entropy.Length);
        }
    }
}