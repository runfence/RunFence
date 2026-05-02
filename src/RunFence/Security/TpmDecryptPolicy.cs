using System.Security.Cryptography;

namespace RunFence.Security;

public class TpmDecryptPolicy(ITpmNativeApi api, TpmHandleProvider handleProvider)
{
    public byte[] DecryptCore(string keyName, byte[] data, int? expectedLength)
    {
        var hProvider = IntPtr.Zero;
        var hKey = IntPtr.Zero;
        try
        {
            hProvider = handleProvider.OpenProvider();

            int status = api.NCryptOpenKey(hProvider, out hKey, keyName, 0, 0);
            if (status != 0)
                throw new CryptographicException($"NCryptOpenKey failed: 0x{status:X8}");

            var paddingInfo = new OaepPaddingInfo
            {
                pszAlgId = TpmNative.BCRYPT_SHA256_ALGORITHM,
                pbLabel = IntPtr.Zero,
                cbLabel = 0
            };

            status = api.NCryptDecrypt(hKey, data, data.Length, ref paddingInfo, null, 0, out int cbResult, TpmNative.BCRYPT_PAD_OAEP);
            ThrowIfPcrMismatch(status, ref hKey);
            if (status != 0)
                throw new CryptographicException($"NCryptDecrypt (size query) failed: 0x{status:X8}");

            var output = new byte[cbResult];
            status = api.NCryptDecrypt(hKey, data, data.Length, ref paddingInfo, output, output.Length, out int actualResult, TpmNative.BCRYPT_PAD_OAEP);
            ThrowIfPcrMismatch(status, ref hKey);
            if (status != 0)
                throw new CryptographicException($"NCryptDecrypt failed: 0x{status:X8}");

            if (expectedLength.HasValue)
                return NormalizeFixedLengthDecryptOutput(output, actualResult, expectedLength.Value);

            return output;
        }
        finally
        {
            handleProvider.FreeIfNonZero(hKey);
            handleProvider.FreeIfNonZero(hProvider);
        }
    }

    private void ThrowIfPcrMismatch(int status, ref IntPtr hKey)
    {
        if (status != TpmNative.NTE_BAD_KEY_STATE)
            return;
        api.NCryptDeleteKey(hKey, 0);
        hKey = IntPtr.Zero;
        throw new CryptographicException("TPM PCR mismatch");
    }

    private static byte[] NormalizeFixedLengthDecryptOutput(byte[] output, int actualResult, int expectedLength)
    {
        if (expectedLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedLength));
        if (actualResult < expectedLength)
            throw new CryptographicException($"NCryptDecrypt returned {actualResult} bytes, expected at least {expectedLength}.");

        for (int i = expectedLength; i < actualResult; i++)
        {
            if (output[i] != 0)
                throw new CryptographicException("NCryptDecrypt returned unexpected non-zero padding beyond the fixed-length plaintext.");
        }

        var result = new byte[expectedLength];
        Buffer.BlockCopy(output, 0, result, 0, expectedLength);
        CryptographicOperations.ZeroMemory(output);
        return result;
    }
}
