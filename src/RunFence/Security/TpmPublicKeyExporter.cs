using System.Security.Cryptography;

namespace RunFence.Security;

public class TpmPublicKeyExporter(ITpmNativeApi api)
{
    public RSA ExportPublicKey(IntPtr hKey)
    {
        int status = api.NCryptExportKey(hKey, IntPtr.Zero, TpmNative.BCRYPT_RSAPUBLIC_BLOB, IntPtr.Zero, null, 0, out int cbResult, 0);
        if (status != 0)
            throw new CryptographicException($"NCryptExportKey (size query) failed: 0x{status:X8}");

        var blob = new byte[cbResult];
        status = api.NCryptExportKey(hKey, IntPtr.Zero, TpmNative.BCRYPT_RSAPUBLIC_BLOB, IntPtr.Zero, blob, blob.Length, out _, 0);
        if (status != 0)
            throw new CryptographicException($"NCryptExportKey failed: 0x{status:X8}");

        // BCRYPT_RSAKEY_BLOB layout (all fields little-endian ULONG):
        //   [0-3]   Magic (BCRYPT_RSAPUBLIC_MAGIC = "RSA1")
        //   [4-7]   BitLength
        //   [8-11]  cbPublicExp
        //   [12-15] cbModulus
        //   [16-19] cbPrime1 (0 in public-only blob)
        //   [20-23] cbPrime2 (0 in public-only blob)
        //   [24..]  PublicExponent bytes, then Modulus bytes
        int cbPublicExp = BitConverter.ToInt32(blob, 8);
        int cbModulus = BitConverter.ToInt32(blob, 12);
        var exponent = new byte[cbPublicExp];
        var modulus = new byte[cbModulus];
        Buffer.BlockCopy(blob, 24, exponent, 0, cbPublicExp);
        Buffer.BlockCopy(blob, 24 + cbPublicExp, modulus, 0, cbModulus);

        var rsa = RSA.Create();
        rsa.ImportParameters(new RSAParameters { Exponent = exponent, Modulus = modulus });
        return rsa;
    }
}
