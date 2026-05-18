using RunFence.Infrastructure;

namespace RunFence.Acl;

public interface IBackupPrivilegeSecurityNative
{
    IntPtr CreateFile(
        string path,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    void CloseHandle(IntPtr handle);

    int GetSecurityInfo(
        IntPtr handle,
        FileSecurityNative.SE_OBJECT_TYPE objectType,
        FileSecurityNative.SECURITY_INFORMATION securityInformation,
        out IntPtr owner,
        out IntPtr group,
        out IntPtr dacl,
        out IntPtr sacl,
        out IntPtr securityDescriptor);

    int SetSecurityInfo(
        IntPtr handle,
        FileSecurityNative.SE_OBJECT_TYPE objectType,
        FileSecurityNative.SECURITY_INFORMATION securityInformation,
        IntPtr owner,
        IntPtr group,
        IntPtr dacl,
        IntPtr sacl);

    uint GetSecurityDescriptorLength(IntPtr securityDescriptor);

    bool GetSecurityDescriptorDacl(
        IntPtr securityDescriptor,
        out bool daclPresent,
        out IntPtr dacl,
        out bool daclDefaulted);

    bool GetSecurityDescriptorOwner(
        IntPtr securityDescriptor,
        out IntPtr owner,
        out bool ownerDefaulted);

    void LocalFree(IntPtr securityDescriptor);
}
