using System.Security.Cryptography;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Persistence;

namespace RunFence.Security;

public class RememberPinService(
    ILoggingService log,
    IMachineIdProvider machineIdProvider,
    IConfigPaths configPaths,
    ITpmKeyProvider tpmKeyProvider,
    IDpapiProtector dpapiProtector)
    : IRememberPinService
{
    // Keep the legacy TPM key name so existing remembered-PIN users remain readable.
    private const string TpmKeyName = "RunFence-AutostartKey";
    private const byte DpapiOnlyVersion = 1;
    private const byte TpmHybridVersion = 2;
    private const int TpmMode = 0x01;
    private const int DpapiMode = 0x00;
    private const int WrappedKeyLengthSize = 2;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int TpmWrappedDataKeyLength = 32;
    // Keep the legacy salt so existing startkey.dat payloads continue to decrypt.
    private static readonly byte[] StaticSalt = "RunFence-startkey-v1"u8.ToArray();

    public bool IsEnabled => File.Exists(configPaths.RememberPinFilePath);

    public bool IsTpmAvailable() => tpmKeyProvider.IsAvailable();

    public void EnableWithTpm(ISecureSecretSnapshotSource pinDerivedKey)
    {
        byte[]? dpapiBlob = null;
        byte[]? dataKey = null;
        byte[]? wrappedDataKey = null;
        byte[]? nonce = null;
        byte[]? ciphertext = null;
        byte[]? tag = null;
        bool keyCreated = false;
        try
        {
            var dpapiEntropy = DeriveDpapiEntropy();
            try
            {
                dpapiBlob = pinDerivedKey.TransformSnapshot(key => dpapiProtector.Protect(key, dpapiEntropy));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(dpapiEntropy);
            }

            dataKey = RandomNumberGenerator.GetBytes(TpmWrappedDataKeyLength);
            nonce = RandomNumberGenerator.GetBytes(NonceLength);
            ciphertext = new byte[dpapiBlob.Length];
            tag = new byte[TagLength];
            using (var aes = new AesGcm(dataKey, TagLength))
            {
                aes.Encrypt(nonce, dpapiBlob, ciphertext, tag);
            }

            // Create the TPM-wrapped key before writing remember-PIN state so a failed key path
            // does not leave an unusable persisted remember-PIN marker behind.
            tpmKeyProvider.CreateKey(TpmKeyName, 2048);
            keyCreated = true;
            wrappedDataKey = tpmKeyProvider.Encrypt(TpmKeyName, dataKey);
            var fileContent = new byte[2 + WrappedKeyLengthSize + wrappedDataKey.Length + NonceLength + TagLength + ciphertext.Length];
            fileContent[0] = TpmHybridVersion;
            fileContent[1] = TpmMode;
            var wrappedKeyLengthBytes = BitConverter.GetBytes((ushort)wrappedDataKey.Length);
            Buffer.BlockCopy(wrappedKeyLengthBytes, 0, fileContent, 2, WrappedKeyLengthSize);
            int offset = 2 + WrappedKeyLengthSize;
            Buffer.BlockCopy(wrappedDataKey, 0, fileContent, offset, wrappedDataKey.Length);
            offset += wrappedDataKey.Length;
            Buffer.BlockCopy(nonce, 0, fileContent, offset, NonceLength);
            offset += NonceLength;
            Buffer.BlockCopy(tag, 0, fileContent, offset, TagLength);
            offset += TagLength;
            Buffer.BlockCopy(ciphertext, 0, fileContent, offset, ciphertext.Length);
            File.WriteAllBytes(configPaths.RememberPinFilePath, fileContent);
        }
        catch
        {
            if (keyCreated)
            {
                try { tpmKeyProvider.DeleteKey(TpmKeyName); }
                catch (CryptographicException) { }
            }
            throw;
        }
        finally
        {
            if (dpapiBlob != null)
                CryptographicOperations.ZeroMemory(dpapiBlob);
            if (dataKey != null)
                CryptographicOperations.ZeroMemory(dataKey);
            if (wrappedDataKey != null)
                CryptographicOperations.ZeroMemory(wrappedDataKey);
            if (nonce != null)
                CryptographicOperations.ZeroMemory(nonce);
            if (ciphertext != null)
                CryptographicOperations.ZeroMemory(ciphertext);
            if (tag != null)
                CryptographicOperations.ZeroMemory(tag);
        }
    }

    public void EnableDpapiOnly(ISecureSecretSnapshotSource pinDerivedKey)
    {
        var dpapiEntropy = DeriveDpapiEntropy();
        byte[]? dpapiBlob = null;
        try
        {
            dpapiBlob = pinDerivedKey.TransformSnapshot(key => dpapiProtector.Protect(key, dpapiEntropy));
            var fileContent = new byte[2 + dpapiBlob.Length];
            fileContent[0] = DpapiOnlyVersion;
            fileContent[1] = DpapiMode;
            Buffer.BlockCopy(dpapiBlob, 0, fileContent, 2, dpapiBlob.Length);
            File.WriteAllBytes(configPaths.RememberPinFilePath, fileContent);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dpapiEntropy);
            if (dpapiBlob != null)
                CryptographicOperations.ZeroMemory(dpapiBlob);
        }
    }

    public bool TryDecryptSecret(out SecureSecret? pinDerivedKey)
    {
        pinDerivedKey = null;
        try
        {
            if (!File.Exists(configPaths.RememberPinFilePath))
                return false;

            var fileContent = File.ReadAllBytes(configPaths.RememberPinFilePath);
            if (fileContent.Length < 2)
            {
                log.Warn("RememberPinService: startkey.dat is too short.");
                return false;
            }

            int version = fileContent[0];
            if (version != DpapiOnlyVersion && version != TpmHybridVersion)
            {
                log.Warn($"RememberPinService: unknown startkey.dat version {version}.");
                return false;
            }

            int mode = fileContent[1];
            byte[]? dpapiBlob = null;
            byte[]? wrappedDataKey = null;
            byte[]? dataKey = null;
            byte[]? nonce = null;
            byte[]? tag = null;
            byte[]? ciphertext = null;
            byte[]? blob = null;
            SecureSecret? decryptedSecret = null;
            try
            {
                if (mode == TpmMode && version == TpmHybridVersion)
                {
                    if (!TryParseTpmHybridPayload(fileContent, out wrappedDataKey, out nonce, out tag, out ciphertext))
                    {
                        log.Warn("RememberPinService: invalid TPM startkey.dat payload.");
                        return false;
                    }

                    try
                    {
                        dataKey = tpmKeyProvider.DecryptExact(TpmKeyName, wrappedDataKey, TpmWrappedDataKeyLength);
                    }
                    catch (CryptographicException ex)
                    {
                        log.Warn($"RememberPinService: TPM decrypt failed (PCR mismatch or key lost): {ex.Message}");
                        return false;
                    }

                    dpapiBlob = new byte[ciphertext!.Length];
                    using (var aes = new AesGcm(dataKey, TagLength))
                    {
                        aes.Decrypt(nonce!, ciphertext, tag!, dpapiBlob);
                    }
                }
                else
                {
                    blob = new byte[fileContent.Length - 2];
                    Buffer.BlockCopy(fileContent, 2, blob, 0, blob.Length);

                    if (mode == TpmMode)
                    {
                        try
                        {
                            dpapiBlob = tpmKeyProvider.Decrypt(TpmKeyName, blob);
                        }
                        catch (CryptographicException ex)
                        {
                            log.Warn($"RememberPinService: TPM decrypt failed (PCR mismatch or key lost): {ex.Message}");
                            return false;
                        }
                    }
                    else
                    {
                        dpapiBlob = blob;
                    }
                }

                var dpapiEntropy = DeriveDpapiEntropy();
                try
                {
                    decryptedSecret = dpapiProtector.UnprotectToSecret(dpapiBlob, dpapiEntropy);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(dpapiEntropy);
                }

                bool validLength = decryptedSecret.TransformSnapshot(data => data.Length == Constants.Argon2OutputBytes);
                if (!validLength)
                {
                    log.Warn("RememberPinService: decrypted PIN key length is invalid.");
                    return false;
                }

                pinDerivedKey = decryptedSecret;
                decryptedSecret = null;
                return true;
            }
            finally
            {
                // Zero the dpapiBlob regardless of mode to avoid leaving DPAPI-encrypted bytes
                // in memory. In TPM mode, dpapiBlob is a freshly decrypted value; in DPAPI-only
                // mode, dpapiBlob == blob (the slice from the file).
                if (dpapiBlob != null)
                    CryptographicOperations.ZeroMemory(dpapiBlob);
                if (wrappedDataKey != null)
                    CryptographicOperations.ZeroMemory(wrappedDataKey);
                if (dataKey != null)
                    CryptographicOperations.ZeroMemory(dataKey);
                if (nonce != null)
                    CryptographicOperations.ZeroMemory(nonce);
                if (tag != null)
                    CryptographicOperations.ZeroMemory(tag);
                if (ciphertext != null)
                    CryptographicOperations.ZeroMemory(ciphertext);
                if (blob != null)
                    CryptographicOperations.ZeroMemory(blob);
                decryptedSecret?.Dispose();
            }
        }
        catch (Exception ex)
        {
            log.Warn($"RememberPinService: TryDecrypt failed: {ex.Message}");
            pinDerivedKey?.Dispose();
            pinDerivedKey = null;
            return false;
        }
    }

    public void Disable()
    {
        var path = configPaths.RememberPinFilePath;
        if (File.Exists(path))
        {
            var content = File.ReadAllBytes(path);
            if (content.Length >= 2 && content[1] == 0x01)
            {
                try { tpmKeyProvider.DeleteKey(TpmKeyName); }
                catch (CryptographicException) { }
            }
        }
        File.Delete(path);
    }

    public void UpdateForPinChange(ISecureSecretSnapshotSource newPinDerivedKey)
    {
        if (!IsEnabled)
            return;

        var fileContent = File.ReadAllBytes(configPaths.RememberPinFilePath);
        if (fileContent.Length < 2 || (fileContent[0] != DpapiOnlyVersion && fileContent[0] != TpmHybridVersion))
            return;

        int mode = fileContent[1];
        if (mode == TpmMode)
        {
            try
            {
                EnableWithTpm(newPinDerivedKey);
            }
            catch
            {
                EnableDpapiOnly(newPinDerivedKey);
            }
        }
        else
        {
            EnableDpapiOnly(newPinDerivedKey);
        }
    }

    private static bool TryParseTpmHybridPayload(
        byte[] fileContent,
        out byte[] wrappedDataKey,
        out byte[] nonce,
        out byte[] tag,
        out byte[] ciphertext)
    {
        wrappedDataKey = [];
        nonce = [];
        tag = [];
        ciphertext = [];

        if (fileContent.Length < 2 + WrappedKeyLengthSize + NonceLength + TagLength)
            return false;

        ushort wrappedKeyLength = BitConverter.ToUInt16(fileContent, 2);
        int headerLength = 2 + WrappedKeyLengthSize;
        int minimumLength = headerLength + wrappedKeyLength + NonceLength + TagLength;
        if (wrappedKeyLength == 0 || fileContent.Length < minimumLength)
            return false;

        int offset = headerLength;
        wrappedDataKey = new byte[wrappedKeyLength];
        Buffer.BlockCopy(fileContent, offset, wrappedDataKey, 0, wrappedKeyLength);
        offset += wrappedKeyLength;

        nonce = new byte[NonceLength];
        Buffer.BlockCopy(fileContent, offset, nonce, 0, NonceLength);
        offset += NonceLength;

        tag = new byte[TagLength];
        Buffer.BlockCopy(fileContent, offset, tag, 0, TagLength);
        offset += TagLength;

        ciphertext = new byte[fileContent.Length - offset];
        if (ciphertext.Length == 0)
            return false;

        Buffer.BlockCopy(fileContent, offset, ciphertext, 0, ciphertext.Length);
        return true;
    }

    private byte[] DeriveDpapiEntropy()
    {
        var identity = machineIdProvider.GetMachineIdentity();
        if (identity.Status != MachineIdentityStatus.Available || identity.MachineIdHash == null)
            throw new InvalidOperationException(identity.ErrorText ?? "Machine identity unavailable for Remember PIN binding.");

        var machineHash = identity.MachineIdHash;
        var combined = new byte[machineHash.Length + StaticSalt.Length];
        try
        {
            Buffer.BlockCopy(machineHash, 0, combined, 0, machineHash.Length);
            Buffer.BlockCopy(StaticSalt, 0, combined, machineHash.Length, StaticSalt.Length);
            return SHA256.HashData(combined);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(combined);
        }
    }
}
