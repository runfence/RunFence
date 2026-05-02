using System.Security.Cryptography;

namespace RunFence.Security;

public class TpmKeyProvider(
    ITpmNativeApi api,
    TpmHandleProvider handleProvider,
    TpmPublicKeyExporter publicKeyExporter,
    TpmKeyCreationPolicy keyCreationPolicy,
    TpmDecryptPolicy decryptPolicy) : ITpmKeyProvider
{
    private bool? _isAvailable;

    public bool IsAvailable()
    {
        if (_isAvailable.HasValue)
            return _isAvailable.Value;

        IntPtr hProvider = IntPtr.Zero;
        try
        {
            int status = api.NCryptOpenStorageProvider(out hProvider, TpmNative.MS_PLATFORM_CRYPTO_PROVIDER, 0);
            _isAvailable = status == 0;
            return _isAvailable.Value;
        }
        catch
        {
            _isAvailable = false;
            return false;
        }
        finally
        {
            handleProvider.FreeIfNonZero(hProvider);
        }
    }

    public void CreateKey(string keyName, int keySize)
        => keyCreationPolicy.CreateKey(keyName, keySize);

    public void DeleteKey(string keyName)
        => keyCreationPolicy.DeleteKey(keyName);

    // MS_PLATFORM_CRYPTO_PROVIDER does not support NCryptEncrypt (NTE_NOT_SUPPORTED on most
    // implementations). RSA encryption is a public-key operation - it does not require TPM
    // involvement. We export the public key and encrypt in software; decryption (private-key
    // operation) still runs inside the TPM via NCryptDecrypt.
    public byte[] Encrypt(string keyName, byte[] data)
        => handleProvider.WithProviderAndKey(keyName, (_, hKey) =>
        {
            using var rsa = publicKeyExporter.ExportPublicKey(hKey);
            return rsa.Encrypt(data, RSAEncryptionPadding.OaepSHA256);
        });

    public byte[] Decrypt(string keyName, byte[] data)
        => decryptPolicy.DecryptCore(keyName, data, expectedLength: null);

    public byte[] DecryptExact(string keyName, byte[] data, int expectedLength)
        => decryptPolicy.DecryptCore(keyName, data, expectedLength);
}
