using RunFence.Infrastructure;

namespace RunFence.Acl;

public class BackupPrivilegeSecurityNative : IBackupPrivilegeSecurityNative
{
    public IntPtr CreateFile(
        string path,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile) =>
        FileSecurityNative.CreateFile(
            path,
            desiredAccess,
            shareMode,
            securityAttributes,
            creationDisposition,
            flagsAndAttributes,
            templateFile);

    public void CloseHandle(IntPtr handle) => ProcessNative.CloseHandle(handle);

    public int GetSecurityInfo(
        IntPtr handle,
        FileSecurityNative.SE_OBJECT_TYPE objectType,
        FileSecurityNative.SECURITY_INFORMATION securityInformation,
        out IntPtr owner,
        out IntPtr group,
        out IntPtr dacl,
        out IntPtr sacl,
        out IntPtr securityDescriptor) =>
        FileSecurityNative.GetSecurityInfo(
            handle,
            objectType,
            securityInformation,
            out owner,
            out group,
            out dacl,
            out sacl,
            out securityDescriptor);

    public int SetSecurityInfo(
        IntPtr handle,
        FileSecurityNative.SE_OBJECT_TYPE objectType,
        FileSecurityNative.SECURITY_INFORMATION securityInformation,
        IntPtr owner,
        IntPtr group,
        IntPtr dacl,
        IntPtr sacl) =>
        FileSecurityNative.SetSecurityInfo(
            handle,
            objectType,
            securityInformation,
            owner,
            group,
            dacl,
            sacl);

    public uint GetSecurityDescriptorLength(IntPtr securityDescriptor) =>
        FileSecurityNative.GetSecurityDescriptorLength(securityDescriptor);

    public bool GetSecurityDescriptorDacl(
        IntPtr securityDescriptor,
        out bool daclPresent,
        out IntPtr dacl,
        out bool daclDefaulted) =>
        FileSecurityNative.GetSecurityDescriptorDacl(
            securityDescriptor,
            out daclPresent,
            out dacl,
            out daclDefaulted);

    public bool GetSecurityDescriptorOwner(
        IntPtr securityDescriptor,
        out IntPtr owner,
        out bool ownerDefaulted) =>
        FileSecurityNative.GetSecurityDescriptorOwner(
            securityDescriptor,
            out owner,
            out ownerDefaulted);

    public void LocalFree(IntPtr securityDescriptor) => ProcessNative.LocalFree(securityDescriptor);
}
