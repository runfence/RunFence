namespace RunFence.Security;

public class TpmNativeApiAdapter : ITpmNativeApi
{
    public int NCryptOpenStorageProvider(out IntPtr phProvider, string pszProviderName, int dwFlags)
        => TpmNative.NCryptOpenStorageProvider(out phProvider, pszProviderName, dwFlags);

    public int NCryptOpenKey(IntPtr hProvider, out IntPtr phKey, string pszKeyName, int dwLegacyKeySpec, int dwFlags)
        => TpmNative.NCryptOpenKey(hProvider, out phKey, pszKeyName, dwLegacyKeySpec, dwFlags);

    public int NCryptCreatePersistedKey(IntPtr hProvider, out IntPtr phKey, string pszAlgId, string? pszKeyName, int dwLegacyKeySpec, int dwFlags)
        => TpmNative.NCryptCreatePersistedKey(hProvider, out phKey, pszAlgId, pszKeyName, dwLegacyKeySpec, dwFlags);

    public int NCryptSetPropertyInt(IntPtr hObject, string pszProperty, ref int pbInput, int cbInput, int dwFlags)
        => TpmNative.NCryptSetProperty(hObject, pszProperty, ref pbInput, cbInput, dwFlags);

    public int NCryptSetPropertyBytes(IntPtr hObject, string pszProperty, byte[] pbInput, int cbInput, int dwFlags)
        => TpmNative.NCryptSetProperty(hObject, pszProperty, pbInput, cbInput, dwFlags);

    public int NCryptFinalizeKey(IntPtr hKey, int dwFlags)
        => TpmNative.NCryptFinalizeKey(hKey, dwFlags);

    public int NCryptExportKey(IntPtr hKey, IntPtr hExportKey, string pszBlobType, IntPtr pParameterList, byte[]? pbOutput, int cbOutput, out int pcbResult, int dwFlags)
        => TpmNative.NCryptExportKey(hKey, hExportKey, pszBlobType, pParameterList, pbOutput, cbOutput, out pcbResult, dwFlags);

    public int NCryptDecrypt(IntPtr hKey, byte[] pbInput, int cbInput, ref OaepPaddingInfo pPaddingInfo, byte[]? pbOutput, int cbOutput, out int pcbResult, int dwFlags)
    {
        var native = new TpmNative.BCRYPT_OAEP_PADDING_INFO
        {
            pszAlgId = pPaddingInfo.pszAlgId,
            pbLabel = pPaddingInfo.pbLabel,
            cbLabel = pPaddingInfo.cbLabel
        };
        return TpmNative.NCryptDecrypt(hKey, pbInput, cbInput, ref native, pbOutput, cbOutput, out pcbResult, dwFlags);
    }

    public int NCryptDeleteKey(IntPtr hKey, int dwFlags)
        => TpmNative.NCryptDeleteKey(hKey, dwFlags);

    public int NCryptFreeObject(IntPtr hObject)
        => TpmNative.NCryptFreeObject(hObject);
}
