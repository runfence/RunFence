using System.Runtime.InteropServices;

namespace RunFence.Security;

internal static class TpmNative
{
    public const string MS_PLATFORM_CRYPTO_PROVIDER = "Microsoft Platform Crypto Provider";
    public const string BCRYPT_RSA_ALGORITHM = "RSA";
    public const string BCRYPT_SHA256_ALGORITHM = "SHA256";
    public const string NCRYPT_LENGTH_PROPERTY = "Length";
    public const string NCRYPT_PCP_PLATFORM_BINDING_PCRMASK_PROPERTY = "PCP_PLATFORM_BINDING_PCRMASK";
    public const int NCRYPT_SILENT_FLAG = 0x00000040;
    public const int BCRYPT_PAD_OAEP = 0x00000004;
    public const int NTE_BAD_KEY_STATE = unchecked((int)0x8009000B);
    public const int NTE_BAD_KEYSET = unchecked((int)0x80090016);
    public const int NTE_NOT_FOUND = unchecked((int)0x80090011);
    public const string BCRYPT_RSAPUBLIC_BLOB = "RSAPUBLICBLOB";

    [StructLayout(LayoutKind.Sequential)]
    public struct BCRYPT_OAEP_PADDING_INFO
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pszAlgId;
        public IntPtr pbLabel;
        public int cbLabel;
    }

    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
    public static extern int NCryptOpenStorageProvider(out IntPtr phProvider, string pszProviderName, int dwFlags);

    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
    public static extern int NCryptCreatePersistedKey(IntPtr hProvider, out IntPtr phKey, string pszAlgId, string? pszKeyName, int dwLegacyKeySpec, int dwFlags);

    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
    public static extern int NCryptOpenKey(IntPtr hProvider, out IntPtr phKey, string pszKeyName, int dwLegacyKeySpec, int dwFlags);

    [DllImport("ncrypt.dll")]
    public static extern int NCryptFinalizeKey(IntPtr hKey, int dwFlags);

    [DllImport("ncrypt.dll")]
    public static extern int NCryptEncrypt(IntPtr hKey, byte[] pbInput, int cbInput, ref BCRYPT_OAEP_PADDING_INFO pPaddingInfo, byte[]? pbOutput, int cbOutput, out int pcbResult, int dwFlags);

    [DllImport("ncrypt.dll")]
    public static extern int NCryptDecrypt(IntPtr hKey, byte[] pbInput, int cbInput, ref BCRYPT_OAEP_PADDING_INFO pPaddingInfo, byte[]? pbOutput, int cbOutput, out int pcbResult, int dwFlags);

    [DllImport("ncrypt.dll")]
    public static extern int NCryptDeleteKey(IntPtr hKey, int dwFlags);

    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
    public static extern int NCryptSetProperty(IntPtr hObject, string pszProperty, byte[] pbInput, int cbInput, int dwFlags);

    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
    public static extern int NCryptSetProperty(IntPtr hObject, string pszProperty, ref int pbInput, int cbInput, int dwFlags);

    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
    public static extern int NCryptExportKey(IntPtr hKey, IntPtr hExportKey, string pszBlobType, IntPtr pParameterList, byte[]? pbOutput, int cbOutput, out int pcbResult, int dwFlags);

    [DllImport("ncrypt.dll")]
    public static extern int NCryptFreeObject(IntPtr hObject);
}
