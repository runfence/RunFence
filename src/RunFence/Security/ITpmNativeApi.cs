using System.Runtime.InteropServices;

namespace RunFence.Security;

[StructLayout(LayoutKind.Sequential)]
public struct OaepPaddingInfo
{
    [MarshalAs(UnmanagedType.LPWStr)]
    public string pszAlgId;
    public IntPtr pbLabel;
    public int cbLabel;
}

public interface ITpmNativeApi
{
    int NCryptOpenStorageProvider(out IntPtr phProvider, string pszProviderName, int dwFlags);
    int NCryptOpenKey(IntPtr hProvider, out IntPtr phKey, string pszKeyName, int dwLegacyKeySpec, int dwFlags);
    int NCryptCreatePersistedKey(IntPtr hProvider, out IntPtr phKey, string pszAlgId, string? pszKeyName, int dwLegacyKeySpec, int dwFlags);
    int NCryptSetPropertyInt(IntPtr hObject, string pszProperty, ref int pbInput, int cbInput, int dwFlags);
    int NCryptSetPropertyBytes(IntPtr hObject, string pszProperty, byte[] pbInput, int cbInput, int dwFlags);
    int NCryptFinalizeKey(IntPtr hKey, int dwFlags);
    int NCryptExportKey(IntPtr hKey, IntPtr hExportKey, string pszBlobType, IntPtr pParameterList, byte[]? pbOutput, int cbOutput, out int pcbResult, int dwFlags);
    int NCryptDecrypt(IntPtr hKey, byte[] pbInput, int cbInput, ref OaepPaddingInfo pPaddingInfo, byte[]? pbOutput, int cbOutput, out int pcbResult, int dwFlags);
    int NCryptDeleteKey(IntPtr hKey, int dwFlags);
    int NCryptFreeObject(IntPtr hObject);
}
