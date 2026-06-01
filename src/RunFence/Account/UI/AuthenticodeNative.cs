using System.Runtime.InteropServices;

namespace RunFence.Account.UI;

internal static class AuthenticodeNative
{
    private static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public static AuthenticodeVerificationResult VerifyFile(string filePath)
    {
        var fileInfoPointer = IntPtr.Zero;
        var data = new WinTrustData();
        try
        {
            var fileInfo = new WinTrustFileInfo(filePath);
            fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
            data.pFile = fileInfoPointer;

            data.dwStateAction = WinTrustData.StateActionVerify;
            var result = WinVerifyTrust(IntPtr.Zero, WintrustActionGenericVerifyV2, data);
            try
            {
                var signerCertificateData = result == 0
                    ? TryReadSignerCertificateData(data.hWvtStateData)
                    : null;
                return new AuthenticodeVerificationResult(result, signerCertificateData);
            }
            finally
            {
                if (data.hWvtStateData != IntPtr.Zero)
                {
                    data.dwStateAction = WinTrustData.StateActionClose;
                    WinVerifyTrust(IntPtr.Zero, WintrustActionGenericVerifyV2, data);
                }
            }
        }
        finally
        {
            if (fileInfoPointer != IntPtr.Zero)
            {
                Marshal.DestroyStructure<WinTrustFileInfo>(fileInfoPointer);
                Marshal.FreeHGlobal(fileInfoPointer);
            }
        }
    }

    private static byte[]? TryReadSignerCertificateData(IntPtr stateData)
    {
        if (stateData == IntPtr.Zero)
            return null;

        var providerDataPointer = WTHelperProvDataFromStateData(stateData);
        if (providerDataPointer == IntPtr.Zero)
            return null;

        var signerPointer = WTHelperGetProvSignerFromChain(providerDataPointer, 0, false, 0);
        if (signerPointer == IntPtr.Zero)
            return null;

        var signer = Marshal.PtrToStructure<CryptProviderSigner>(signerPointer);
        if (signer.csCertChain == 0 || signer.pasCertChain == IntPtr.Zero)
            return null;

        var providerCertificate = Marshal.PtrToStructure<CryptProviderCertificate>(signer.pasCertChain);
        if (providerCertificate.pCert == IntPtr.Zero)
            return null;

        var certificateContext = Marshal.PtrToStructure<CertificateContext>(providerCertificate.pCert);
        if (certificateContext.pbCertEncoded == IntPtr.Zero || certificateContext.cbCertEncoded == 0)
            return null;

        var certificateData = new byte[certificateContext.cbCertEncoded];
        Marshal.Copy(certificateContext.pbCertEncoded, certificateData, 0, certificateData.Length);
        return certificateData;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WinVerifyTrust(
        IntPtr hwnd,
        [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionId,
        WinTrustData pWvtData);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperProvDataFromStateData(IntPtr hStateData);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperGetProvSignerFromChain(
        IntPtr pProvData,
        uint idxSigner,
        [MarshalAs(UnmanagedType.Bool)] bool fCounterSigner,
        uint idxCounterSigner);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustFileInfo(string filePath)
    {
        public uint cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>();
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pcwszFilePath = filePath;
        public IntPtr hFile = IntPtr.Zero;
        public IntPtr pgKnownSubject = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustData
    {
        public const uint StateActionVerify = 1;
        public const uint StateActionClose = 2;

        public uint cbStruct = (uint)Marshal.SizeOf<WinTrustData>();
        public IntPtr pPolicyCallbackData = IntPtr.Zero;
        public IntPtr pSipClientData = IntPtr.Zero;
        public uint dwUIChoice = 2;
        public uint fdwRevocationChecks = 1;
        public uint dwUnionChoice = 1;
        public IntPtr pFile = IntPtr.Zero;
        public uint dwStateAction;
        public IntPtr hWvtStateData = IntPtr.Zero;
        public IntPtr pwszUrlReference = IntPtr.Zero;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CryptProviderSigner
    {
        public uint cbStruct;
        public NativeFileTime sftVerifyAsOf;
        public uint csCertChain;
        public IntPtr pasCertChain;
        public uint dwSignerType;
        public IntPtr psSigner;
        public uint dwError;
        public uint csCounterSigners;
        public IntPtr pasCounterSigners;
        public IntPtr pChainContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CryptProviderCertificate
    {
        public uint cbStruct;
        public IntPtr pCert;
        public int fCommercial;
        public int fTrustedRoot;
        public int fSelfSigned;
        public int fTestCert;
        public uint dwRevokedReason;
        public uint dwConfidence;
        public uint dwError;
        public IntPtr pTrustListContext;
        public int fTrustListSignerCert;
        public IntPtr pCtlContext;
        public uint dwCtlError;
        public int fIsCyclic;
        public IntPtr pChainElement;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CertificateContext
    {
        public uint dwCertEncodingType;
        public IntPtr pbCertEncoded;
        public uint cbCertEncoded;
        public IntPtr pCertInfo;
        public IntPtr hCertStore;
    }
}
