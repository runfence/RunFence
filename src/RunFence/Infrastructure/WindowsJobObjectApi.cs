using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace RunFence.Infrastructure;

public sealed class WindowsJobObjectApi : IJobObjectApi
{
    public IntPtr CreateJobObject(string? name, string? securityDescriptorSddl) =>
        JobNative.CreateJobObject(name, securityDescriptorSddl);

    public IntPtr OpenJobObject(uint desiredAccess, bool inheritHandle, string name) =>
        JobNative.OpenJobObject(desiredAccess, inheritHandle, name);

    public bool AssignProcessToJobObject(IntPtr jobHandle, IntPtr processHandle) =>
        JobNative.AssignProcessToJobObject(jobHandle, processHandle);

    public int GetProcessId(IntPtr processHandle) =>
        (int)ProcessNative.GetProcessId(processHandle);

    public bool SetInformationJobObject(IntPtr jobHandle, int infoClass, IntPtr info, uint infoLength) =>
        JobNative.SetInformationJobObject(jobHandle, infoClass, info, infoLength);

    public bool QueryInformationJobObject(IntPtr jobHandle, int infoClass, IntPtr info, uint infoLength, out uint returnLength) =>
        JobNative.QueryInformationJobObject(jobHandle, infoClass, info, infoLength, out returnLength);

    public bool SetUiRestrictions(IntPtr jobHandle, uint flags) =>
        JobNative.SetUiRestrictions(jobHandle, flags);

    public uint? QueryUiRestrictions(IntPtr jobHandle) =>
        JobNative.QueryUiRestrictions(jobHandle);

    public HashSet<int>? QueryProcessIds(IntPtr jobHandle) =>
        JobNative.QueryProcessIds(jobHandle);

    public bool DuplicateHandleToProcess(
        IntPtr sourceProcessHandle,
        IntPtr sourceHandle,
        IntPtr targetProcessHandle,
        uint desiredAccess) =>
        JobNative.DuplicateHandle(sourceProcessHandle, sourceHandle, targetProcessHandle,
            out _, desiredAccess, false, 0);

    public JobObjectSecuritySnapshot? GetSecuritySnapshot(IntPtr jobHandle)
    {
        var error = FileSecurityNative.GetSecurityInfo(
            jobHandle,
            FileSecurityNative.SE_OBJECT_TYPE.SE_KERNEL_OBJECT,
            FileSecurityNative.SECURITY_INFORMATION.OWNER_SECURITY_INFORMATION
            | FileSecurityNative.SECURITY_INFORMATION.DACL_SECURITY_INFORMATION,
            out _,
            out _,
            out _,
            out _,
            out var securityDescriptor);
        if (error != 0 || securityDescriptor == IntPtr.Zero)
            return null;

        try
        {
            if (!FileSecurityNative.ConvertSecurityDescriptorToStringSecurityDescriptor(
                    securityDescriptor,
                    1,
                    FileSecurityNative.SECURITY_INFORMATION.OWNER_SECURITY_INFORMATION
                    | FileSecurityNative.SECURITY_INFORMATION.DACL_SECURITY_INFORMATION,
                    out var sddlPointer,
                    out _))
                return null;

            try
            {
                var sddl = Marshal.PtrToStringUni(sddlPointer);
                if (string.IsNullOrWhiteSpace(sddl))
                    return null;

                var raw = new RawSecurityDescriptor(sddl);
                var entries = new List<JobObjectAccessEntry>();
                if (raw.DiscretionaryAcl != null)
                {
                    foreach (GenericAce ace in raw.DiscretionaryAcl)
                    {
                        if (ace is not CommonAce commonAce)
                            continue;
                        entries.Add(new JobObjectAccessEntry(
                            commonAce.SecurityIdentifier,
                            commonAce.AccessMask,
                            commonAce.AceQualifier == AceQualifier.AccessAllowed));
                    }
                }

                return new JobObjectSecuritySnapshot(raw.Owner, entries);
            }
            finally
            {
                ProcessNative.LocalFree(sddlPointer);
            }
        }
        finally
        {
            ProcessNative.LocalFree(securityDescriptor);
        }
    }

    public void CloseHandle(IntPtr handle) => ProcessNative.CloseHandle(handle);

    public int GetLastWin32Error() => Marshal.GetLastWin32Error();
}
