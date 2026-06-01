using System.Runtime.InteropServices;
using System.Security.AccessControl;

namespace RunFence.Infrastructure;

public sealed class WindowsJobObjectApi(IWindowsJobObjectNative native) : IJobObjectApi
{
    private const int JobObjectBasicProcessIdList = 3;
    private const int JobObjectBasicUIRestrictions = 4;
    private const int JobObjectExtendedLimitInformation = 9;
    private const int ErrorMoreData = 234;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public IntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    public IntPtr CreateJobObject(string? name, string? securityDescriptorSddl)
    {
        if (string.IsNullOrWhiteSpace(securityDescriptorSddl))
            return native.CreateJobObject(name);

        return native.CreateJobObjectWithSecurityDescriptor(name, securityDescriptorSddl);
    }

    public IntPtr OpenJobObject(uint desiredAccess, bool inheritHandle, string name) =>
        native.OpenJobObject(desiredAccess, inheritHandle, name);

    public bool AssignProcessToJobObject(IntPtr jobHandle, IntPtr processHandle) =>
        native.AssignProcessToJobObject(jobHandle, processHandle);

    public bool? IsProcessInJob(IntPtr processHandle, IntPtr jobHandle)
    {
        if (!native.IsProcessInJob(processHandle, jobHandle, out var result))
            return null;

        return result;
    }

    public bool AreSameJobObject(IntPtr firstJobHandle, IntPtr secondJobHandle) =>
        native.CompareObjectHandles(firstJobHandle, secondJobHandle);

    public int GetProcessId(IntPtr processHandle) =>
        native.GetProcessId(processHandle);

    public bool SetUiRestrictions(IntPtr jobHandle, uint flags)
    {
        var buffer = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(buffer, (int)flags);
            return native.SetInformationJobObject(jobHandle, JobObjectBasicUIRestrictions, buffer, 4);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public uint? QueryUiRestrictions(IntPtr jobHandle)
    {
        var buffer = Marshal.AllocHGlobal(4);
        try
        {
            if (!native.QueryInformationJobObject(jobHandle, JobObjectBasicUIRestrictions, buffer, 4, out _))
                return null;

            return unchecked((uint)Marshal.ReadInt32(buffer));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public uint? QueryBasicLimitFlags(IntPtr jobHandle)
    {
        var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!native.QueryInformationJobObject(
                    jobHandle,
                    JobObjectExtendedLimitInformation,
                    buffer,
                    (uint)size,
                    out _))
            {
                return null;
            }

            var info = Marshal.PtrToStructure<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>(buffer);
            return info.BasicLimitInformation.LimitFlags;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public HashSet<int>? QueryProcessIds(IntPtr jobHandle)
    {
        const int headerSize = 8;
        const int initialCapacity = 64;
        int bufferSize = headerSize + initialCapacity * IntPtr.Size;
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            if (!native.QueryInformationJobObject(jobHandle, JobObjectBasicProcessIdList, buffer, (uint)bufferSize, out _))
            {
                if (native.GetLastWin32Error() != ErrorMoreData)
                    return null;

                int actualCount = Marshal.ReadInt32(buffer); // NumberOfAssignedProcesses
                Marshal.FreeHGlobal(buffer);
                buffer = IntPtr.Zero;
                bufferSize = headerSize + actualCount * IntPtr.Size;
                buffer = Marshal.AllocHGlobal(bufferSize);
                if (!native.QueryInformationJobObject(jobHandle, JobObjectBasicProcessIdList, buffer, (uint)bufferSize, out _))
                    return null;
            }

            int count = Marshal.ReadInt32(buffer, 4); // NumberOfProcessIdsInList
            var result = new HashSet<int>(count);
            for (int i = 0; i < count; i++)
                result.Add((int)Marshal.ReadIntPtr(buffer, headerSize + i * IntPtr.Size));
            return result;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(buffer);
        }
    }

    public bool DuplicateHandleToProcess(
        IntPtr sourceProcessHandle,
        IntPtr sourceHandle,
        IntPtr targetProcessHandle,
        uint desiredAccess,
        out IntPtr duplicatedTargetHandle) =>
        native.DuplicateHandleToProcess(
            sourceProcessHandle,
            sourceHandle,
            targetProcessHandle,
            desiredAccess,
            out duplicatedTargetHandle);

    public JobObjectSecuritySnapshot? GetSecuritySnapshot(IntPtr jobHandle)
    {
        var error = native.GetSecurityInfo(
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
            if (!native.ConvertSecurityDescriptorToStringSecurityDescriptor(
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

                return new JobObjectSecuritySnapshot(raw.Owner, raw.DiscretionaryAcl != null, entries);
            }
            finally
            {
                native.LocalFree(sddlPointer);
            }
        }
        finally
        {
            native.LocalFree(securityDescriptor);
        }
    }

    public void CloseHandle(IntPtr handle) => native.CloseHandle(handle);

    public int GetLastWin32Error() => native.GetLastWin32Error();
}
