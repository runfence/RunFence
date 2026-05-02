using System.Runtime.InteropServices;

namespace RunFence.Infrastructure;

public static class JobNative
{
    private const int JobObjectBasicProcessIdList = 3;
    private const int JobObjectBasicUIRestrictions = 4;
    private const int ERROR_MORE_DATA = 234;

    public const uint JOB_OBJECT_UILIMIT_HANDLES = 0x0001;
    public const uint JOB_OBJECT_UILIMIT_DISPLAYSETTINGS = 0x0010;
    public const uint JOB_OBJECT_UILIMIT_DESKTOP = 0x0040;
    public const uint JOB_OBJECT_UILIMIT_EXITWINDOWS = 0x0080;
    public const uint JOB_OBJECT_UILIMIT_SYSTEMPARAMETERS = 0x0008;
    public const uint JOB_OBJECT_QUERY = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool bInheritHandle;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(ref SECURITY_ATTRIBUTES lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr OpenJobObject(uint dwDesiredAccess, bool bInheritHandle, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool QueryInformationJobObject(IntPtr hJob, int JobObjectInfoClass,
        IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength, out uint lpReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInfoClass,
        IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DuplicateHandle(
        IntPtr hSourceProcessHandle,
        IntPtr hSourceHandle,
        IntPtr hTargetProcessHandle,
        out IntPtr lpTargetHandle,
        uint dwDesiredAccess,
        bool bInheritHandle,
        uint dwOptions);

    public static IntPtr CreateJobObject(string? name, string? securityDescriptorSddl)
    {
        if (string.IsNullOrWhiteSpace(securityDescriptorSddl))
            return CreateJobObject(IntPtr.Zero, name);

        if (!FileSecurityNative.ConvertStringSecurityDescriptorToSecurityDescriptor(
                securityDescriptorSddl, 1, out var sd, out _))
            return IntPtr.Zero;

        try
        {
            var attrs = new SECURITY_ATTRIBUTES
            {
                nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
                lpSecurityDescriptor = sd,
                bInheritHandle = false,
            };
            return CreateJobObject(ref attrs, name);
        }
        finally
        {
            ProcessNative.LocalFree(sd);
        }
    }

    public static bool SetUiRestrictions(IntPtr hJob, uint flags)
    {
        var buffer = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(buffer, (int)flags);
            return SetInformationJobObject(hJob, JobObjectBasicUIRestrictions, buffer, 4);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static uint? QueryUiRestrictions(IntPtr hJob)
    {
        var buffer = Marshal.AllocHGlobal(4);
        try
        {
            if (!QueryInformationJobObject(hJob, JobObjectBasicUIRestrictions, buffer, 4, out _))
                return null;
            return unchecked((uint)Marshal.ReadInt32(buffer));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Returns all PIDs currently in the job, or null on failure.
    /// Uses JOBOBJECT_BASIC_PROCESS_ID_LIST (class 3):
    ///   DWORD NumberOfAssignedProcesses  (offset 0)
    ///   DWORD NumberOfProcessIdsInList   (offset 4)
    ///   ULONG_PTR ProcessIdList[...]     (offset 8)
    /// </summary>
    public static HashSet<int>? QueryProcessIds(IntPtr hJob)
    {
        const int headerSize = 8;
        const int initialCapacity = 64;
        int bufferSize = headerSize + initialCapacity * IntPtr.Size;
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            if (!QueryInformationJobObject(hJob, JobObjectBasicProcessIdList, buffer, (uint)bufferSize, out _))
            {
                if (Marshal.GetLastWin32Error() != ERROR_MORE_DATA)
                    return null;
                int actualCount = Marshal.ReadInt32(buffer); // NumberOfAssignedProcesses
                Marshal.FreeHGlobal(buffer);
                buffer = IntPtr.Zero;
                bufferSize = headerSize + actualCount * IntPtr.Size;
                buffer = Marshal.AllocHGlobal(bufferSize);
                if (!QueryInformationJobObject(hJob, JobObjectBasicProcessIdList, buffer, (uint)bufferSize, out _))
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
}
