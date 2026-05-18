using System.Security.Cryptography;
using RunFence.Core;

namespace RunFence.Security;

public class TpmKeyCreationPolicy(ITpmNativeApi api, TpmHandleProvider handleProvider, ILoggingService log)
{
    // PCP_PLATFORM_BINDING_PCRMASK expects the TPM2 PCR selection bytes, not a DWORD.
    private static readonly byte[] Pcr7Mask = [0x80, 0x00, 0x00];

    public void CreateKey(string keyName, int keySize)
    {
        DeleteKeyIfExists(keyName);

        int pcrStatus = CreateKeyCore(keyName, keySize, bindToPcr: true);
        if (pcrStatus == 0)
            return;

        // Retry without PCR binding is intentional compatibility behavior when the preferred PCR path is unavailable.
        log.Warn($"TpmKeyProvider: PCR binding failed (0x{pcrStatus:X8}), retrying without PCR binding.");
        DeleteKeyIfExists(keyName);
        CreateKeyCore(keyName, keySize, bindToPcr: false);
    }

    public void DeleteKey(string keyName)
    {
        DeleteKeyCore(keyName, ignoreNotFound: false);
    }

    private int CreateKeyCore(string keyName, int keySize, bool bindToPcr)
    {
        var hProvider = IntPtr.Zero;
        var hKey = IntPtr.Zero;
        var keyFinalized = false;
        try
        {
            hProvider = handleProvider.OpenProvider();
            hKey = CreatePersistedKeyHandle(hProvider, keyName, keySize);

            if (bindToPcr)
            {
                int pcrStatus = TrySetPcrBinding(hKey);
                if (pcrStatus != 0)
                {
                    TryDeleteKeyHandle(ref hKey);
                    return pcrStatus;
                }
            }

            int finalizeStatus = api.NCryptFinalizeKey(hKey, TpmNative.NCRYPT_SILENT_FLAG);
            if (finalizeStatus != 0)
                throw new CryptographicException($"NCryptFinalizeKey failed: 0x{finalizeStatus:X8}");

            keyFinalized = true;
            return 0;
        }
        catch
        {
            if (!keyFinalized)
                TryDeleteKeyHandle(ref hKey);
            throw;
        }
        finally
        {
            handleProvider.FreeIfNonZero(hKey);
            handleProvider.FreeIfNonZero(hProvider);
        }
    }

    private IntPtr CreatePersistedKeyHandle(IntPtr hProvider, string keyName, int keySize)
    {
        int createStatus = api.NCryptCreatePersistedKey(hProvider, out var hKey, TpmNative.BCRYPT_RSA_ALGORITHM, keyName, 0, 0);
        if (createStatus != 0)
            throw new CryptographicException($"NCryptCreatePersistedKey failed: 0x{createStatus:X8}");

        int lengthStatus = api.NCryptSetPropertyInt(hKey, TpmNative.NCRYPT_LENGTH_PROPERTY, ref keySize, 4, 0);
        if (lengthStatus != 0)
        {
            TryDeleteKeyHandle(ref hKey);
            throw new CryptographicException($"NCryptSetProperty(Length) failed: 0x{lengthStatus:X8}");
        }

        return hKey;
    }

    private int TrySetPcrBinding(IntPtr hKey)
        => api.NCryptSetPropertyBytes(hKey, TpmNative.NCRYPT_PCP_PLATFORM_BINDING_PCRMASK_PROPERTY, Pcr7Mask, Pcr7Mask.Length, 0);

    private void DeleteKeyIfExists(string keyName)
    {
        DeleteKeyCore(keyName, ignoreNotFound: true);
    }

    private void DeleteKeyCore(string keyName, bool ignoreNotFound)
    {
        var hProvider = IntPtr.Zero;
        var hKey = IntPtr.Zero;
        try
        {
            hProvider = handleProvider.OpenProvider();

            int openStatus = api.NCryptOpenKey(hProvider, out hKey, keyName, 0, 0);
            if ((openStatus == TpmNative.NTE_NOT_FOUND || openStatus == TpmNative.NTE_BAD_KEYSET) && ignoreNotFound)
                return;
            if (openStatus == TpmNative.NTE_NOT_FOUND || openStatus == TpmNative.NTE_BAD_KEYSET)
                throw new CryptographicException($"NCryptOpenKey failed: 0x{openStatus:X8}");
            if (openStatus != 0)
                throw new CryptographicException($"NCryptOpenKey failed: 0x{openStatus:X8}");

            int deleteStatus = api.NCryptDeleteKey(hKey, 0);
            hKey = IntPtr.Zero;
            if (deleteStatus != 0)
                throw new CryptographicException($"NCryptDeleteKey failed: 0x{deleteStatus:X8}");
        }
        finally
        {
            handleProvider.FreeIfNonZero(hKey);
            handleProvider.FreeIfNonZero(hProvider);
        }
    }

    private void TryDeleteKeyHandle(ref IntPtr hKey)
    {
        if (hKey == IntPtr.Zero)
            return;

        try
        {
            int deleteStatus = api.NCryptDeleteKey(hKey, 0);
            if (deleteStatus == 0)
                hKey = IntPtr.Zero;
        }
        catch
        {
        }
    }
}
