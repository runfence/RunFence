using System.Runtime.InteropServices;

namespace RunFence.TokenTest.Native;

internal static class SecurityNative
{
    public const uint WINSTA_ALL_ACCESS = 0x37F;
    public const uint DESKTOP_ALL_ACCESS = 0x1FF;
    public const uint GENERIC_ALL = 0x10000000;
    public const uint STANDARD_RIGHTS_ALL = 0x001F0000;
    public const uint READ_CONTROL = 0x00020000;
    public const uint WRITE_DAC = 0x00040000;
    public const uint WRITE_OWNER = 0x00080000;
    public const uint DACL_SECURITY_INFORMATION = 0x4;
    public const uint LABEL_SECURITY_INFORMATION = 0x00000010;
    public const uint SYSTEM_MANDATORY_LABEL_NO_WRITE_UP = 0x1;
    public const byte SYSTEM_MANDATORY_LABEL_ACE_TYPE = 0x11;

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool CreateWellKnownSid(
        WELL_KNOWN_SID_TYPE WellKnownSidType,
        IntPtr DomainSid,
        IntPtr pSid,
        ref uint cbSid);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool ConvertSidToStringSid(IntPtr pSid, out string stringSid);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern uint GetSecurityInfo(
        IntPtr handle,
        SE_OBJECT_TYPE ObjectType,
        uint SecurityInfo,
        IntPtr ppsidOwner,
        IntPtr ppsidGroup,
        out IntPtr ppDacl,
        IntPtr ppSacl,
        out IntPtr ppSecurityDescriptor);

    // Overload for reading SACL (LABEL_SECURITY_INFORMATION); ppDacl not retrieved.
    [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "GetSecurityInfo")]
    public static extern uint GetSecurityInfoWithSacl(
        IntPtr handle,
        SE_OBJECT_TYPE ObjectType,
        uint SecurityInfo,
        IntPtr ppsidOwner,
        IntPtr ppsidGroup,
        IntPtr ppDacl,
        out IntPtr ppSacl,
        out IntPtr ppSecurityDescriptor);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern uint SetSecurityInfo(
        IntPtr handle,
        SE_OBJECT_TYPE ObjectType,
        uint SecurityInfo,
        IntPtr psidOwner,
        IntPtr psidGroup,
        IntPtr pDacl,
        IntPtr pSacl);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern uint SetEntriesInAcl(
        uint cCountOfExplicitEntries,
        [MarshalAs(UnmanagedType.LPArray)] EXPLICIT_ACCESS[] pListOfExplicitEntries,
        IntPtr OldAcl,
        out IntPtr NewAcl);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool GetAclInformation(
        IntPtr pAcl,
        ref ACL_SIZE_INFORMATION pAclInformation,
        uint nAclInformationLength,
        ACL_INFORMATION_CLASS dwAclInformationClass);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool GetAce(IntPtr pAcl, uint dwAceIndex, out IntPtr pAce);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool InitializeAcl(IntPtr pAcl, uint nAclLength, uint dwAclRevision);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool AddMandatoryAce(
        IntPtr pAcl,
        uint dwAclRevision,
        uint AceFlags,
        uint MandatoryPolicy,
        IntPtr pLabelSid);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ConvertStringSidToSid(string StringSid, out IntPtr Sid);

    [DllImport("kernel32.dll")]
    public static extern IntPtr LocalFree(IntPtr hMem);

    [DllImport("advapi32.dll")]
    public static extern void FreeSid(IntPtr pSid);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern int GetLengthSid(IntPtr pSid);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool CopySid(uint nDestinationSidLength, IntPtr pDestinationSid, IntPtr pSourceSid);

    [StructLayout(LayoutKind.Sequential)]
    public struct EXPLICIT_ACCESS
    {
        public uint grfAccessPermissions;
        public ACCESS_MODE grfAccessMode;
        public uint grfInheritance;
        public TRUSTEE Trustee;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TRUSTEE
    {
        public IntPtr pMultipleTrustee;
        public MULTIPLE_TRUSTEE_OPERATION MultipleTrusteeOperation;
        public TRUSTEE_FORM TrusteeForm;
        public TRUSTEE_TYPE TrusteeType;
        public IntPtr ptstrName; // used as SID pointer when TrusteeForm = TRUSTEE_IS_SID
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ACL_SIZE_INFORMATION
    {
        public uint AceCount;
        public uint AclBytesInUse;
        public uint AclBytesFree;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ACE_HEADER
    {
        public byte AceType;
        public byte AceFlags;
        public ushort AceSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ACCESS_ALLOWED_ACE
    {
        public ACE_HEADER Header;
        public uint Mask;
        public uint SidStart;
    }

    public const byte ACCESS_ALLOWED_ACE_TYPE = 0;
    public const byte ACCESS_DENIED_ACE_TYPE = 1;

    public enum WELL_KNOWN_SID_TYPE
    {
        WinWorldSid = 1,
        WinAuthenticatedUserSid = 17,
        WinBuiltinAdministratorsSid = 26,
        WinBuiltinUsersSid = 27,
        WinMediumLabelSid = 67,
    }

    public enum SE_OBJECT_TYPE
    {
        SE_UNKNOWN_OBJECT_TYPE = 0,
        SE_FILE_OBJECT,
        SE_SERVICE,
        SE_PRINTER,
        SE_REGISTRY_KEY,
        SE_LMSHARE,
        SE_KERNEL_OBJECT,
        SE_WINDOW_OBJECT,
        SE_DS_OBJECT,
        SE_DS_OBJECT_ALL,
        SE_PROVIDER_DEFINED_OBJECT,
        SE_WMIGUID_OBJECT,
        SE_REGISTRY_WOW64_32KEY,
        SE_REGISTRY_WOW64_64KEY,
    }

    public enum TRUSTEE_FORM
    {
        TRUSTEE_IS_SID,
        TRUSTEE_IS_NAME,
        TRUSTEE_BAD_FORM,
        TRUSTEE_IS_OBJECTS_AND_SID,
        TRUSTEE_IS_OBJECTS_AND_NAME
    }

    public enum TRUSTEE_TYPE
    {
        TRUSTEE_IS_UNKNOWN,
        TRUSTEE_IS_USER,
        TRUSTEE_IS_GROUP,
        TRUSTEE_IS_DOMAIN,
        TRUSTEE_IS_ALIAS,
        TRUSTEE_IS_WELL_KNOWN_GROUP,
        TRUSTEE_IS_DELETED,
        TRUSTEE_IS_INVALID,
        TRUSTEE_IS_COMPUTER
    }

    public enum ACCESS_MODE
    {
        NOT_USED_ACCESS = 0,
        GRANT_ACCESS,
        SET_ACCESS,
        DENY_ACCESS,
        REVOKE_ACCESS,
        SET_AUDIT_SUCCESS,
        SET_AUDIT_FAILURE
    }

    public enum MULTIPLE_TRUSTEE_OPERATION
    {
        NO_MULTIPLE_TRUSTEE,
        TRUSTEE_IS_IMPERSONATE
    }

    public enum ACL_INFORMATION_CLASS
    {
        AclRevisionInformation = 1,
        AclSizeInformation
    }
}
