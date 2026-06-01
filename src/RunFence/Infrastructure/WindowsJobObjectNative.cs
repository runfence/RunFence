using System.Runtime.InteropServices;

namespace RunFence.Infrastructure;

public sealed class WindowsJobObjectNative : IWindowsJobObjectNative
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool bInheritHandle;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(ref SECURITY_ATTRIBUTES jobAttributes, string? name);

    [DllImport("kernel32.dll", EntryPoint = "OpenJobObject", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenJobObjectNative(uint desiredAccess, bool inheritHandle, string name);

    [DllImport("kernel32.dll", EntryPoint = "AssignProcessToJobObject", SetLastError = true)]
    private static extern bool AssignProcessToJobObjectNative(IntPtr jobHandle, IntPtr processHandle);

    [DllImport("kernel32.dll", EntryPoint = "IsProcessInJob", SetLastError = true)]
    private static extern bool IsProcessInJobNative(IntPtr processHandle, IntPtr jobHandle, out bool result);

    [DllImport("kernelbase.dll", EntryPoint = "CompareObjectHandles")]
    private static extern bool CompareObjectHandlesNative(IntPtr firstHandle, IntPtr secondHandle);

    [DllImport("kernel32.dll", EntryPoint = "QueryInformationJobObject", SetLastError = true)]
    private static extern bool QueryInformationJobObjectNative(
        IntPtr jobHandle,
        int jobObjectInfoClass,
        IntPtr buffer,
        uint bufferLength,
        out uint returnLength);

    [DllImport("kernel32.dll", EntryPoint = "SetInformationJobObject", SetLastError = true)]
    private static extern bool SetInformationJobObjectNative(
        IntPtr jobHandle,
        int jobObjectInfoClass,
        IntPtr buffer,
        uint bufferLength);

    public IntPtr CreateJobObject(string? name) => CreateJobObject(IntPtr.Zero, name);

    public IntPtr CreateJobObjectWithSecurityDescriptor(string? name, string securityDescriptorSddl)
    {
        if (!FileSecurityNative.ConvertStringSecurityDescriptorToSecurityDescriptor(
                securityDescriptorSddl,
                1,
                out var securityDescriptor,
                out _))
        {
            return IntPtr.Zero;
        }

        try
        {
            var attrs = new SECURITY_ATTRIBUTES
            {
                nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
                lpSecurityDescriptor = securityDescriptor,
                bInheritHandle = false,
            };
            return CreateJobObject(ref attrs, name);
        }
        finally
        {
            ProcessNative.LocalFree(securityDescriptor);
        }
    }

    public IntPtr OpenJobObject(uint desiredAccess, bool inheritHandle, string name) =>
        OpenJobObjectNative(desiredAccess, inheritHandle, name);

    public bool AssignProcessToJobObject(IntPtr jobHandle, IntPtr processHandle) =>
        AssignProcessToJobObjectNative(jobHandle, processHandle);

    public bool IsProcessInJob(IntPtr processHandle, IntPtr jobHandle, out bool result) =>
        IsProcessInJobNative(processHandle, jobHandle, out result);

    public bool CompareObjectHandles(IntPtr firstHandle, IntPtr secondHandle) =>
        CompareObjectHandlesNative(firstHandle, secondHandle);

    public bool QueryInformationJobObject(
        IntPtr jobHandle,
        int jobObjectInfoClass,
        IntPtr buffer,
        uint bufferLength,
        out uint returnLength) =>
        QueryInformationJobObjectNative(jobHandle, jobObjectInfoClass, buffer, bufferLength, out returnLength);

    public bool SetInformationJobObject(
        IntPtr jobHandle,
        int jobObjectInfoClass,
        IntPtr buffer,
        uint bufferLength) =>
        SetInformationJobObjectNative(jobHandle, jobObjectInfoClass, buffer, bufferLength);

    public bool DuplicateHandleToProcess(
        IntPtr sourceProcessHandle,
        IntPtr sourceHandle,
        IntPtr targetProcessHandle,
        uint desiredAccess,
        out IntPtr duplicatedTargetHandle) =>
        JobNative.DuplicateHandle(
            sourceProcessHandle,
            sourceHandle,
            targetProcessHandle,
            out duplicatedTargetHandle,
            desiredAccess,
            false,
            0);

    public int GetSecurityInfo(
        IntPtr handle,
        FileSecurityNative.SE_OBJECT_TYPE objectType,
        FileSecurityNative.SECURITY_INFORMATION securityInformation,
        out IntPtr ownerSid,
        out IntPtr groupSid,
        out IntPtr dacl,
        out IntPtr sacl,
        out IntPtr securityDescriptor) =>
        FileSecurityNative.GetSecurityInfo(
            handle,
            objectType,
            securityInformation,
            out ownerSid,
            out groupSid,
            out dacl,
            out sacl,
            out securityDescriptor);

    public bool ConvertSecurityDescriptorToStringSecurityDescriptor(
        IntPtr securityDescriptor,
        uint revision,
        FileSecurityNative.SECURITY_INFORMATION securityInformation,
        out IntPtr stringSecurityDescriptor,
        out uint stringSecurityDescriptorLength) =>
        FileSecurityNative.ConvertSecurityDescriptorToStringSecurityDescriptor(
            securityDescriptor,
            revision,
            securityInformation,
            out stringSecurityDescriptor,
            out stringSecurityDescriptorLength);

    public IntPtr LocalFree(IntPtr memory) => ProcessNative.LocalFree(memory);

    public void CloseHandle(IntPtr handle) => ProcessNative.CloseHandle(handle);

    public int GetLastWin32Error() => Marshal.GetLastWin32Error();

    public int GetProcessId(IntPtr processHandle) => (int)ProcessNative.GetProcessId(processHandle);
}
