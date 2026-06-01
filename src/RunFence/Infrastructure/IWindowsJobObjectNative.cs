namespace RunFence.Infrastructure;

public interface IWindowsJobObjectNative
{
    IntPtr CreateJobObject(string? name);
    IntPtr CreateJobObjectWithSecurityDescriptor(string? name, string securityDescriptorSddl);
    IntPtr OpenJobObject(uint desiredAccess, bool inheritHandle, string name);
    bool AssignProcessToJobObject(IntPtr jobHandle, IntPtr processHandle);
    bool IsProcessInJob(IntPtr processHandle, IntPtr jobHandle, out bool result);
    bool CompareObjectHandles(IntPtr firstHandle, IntPtr secondHandle);
    bool QueryInformationJobObject(
        IntPtr jobHandle,
        int jobObjectInfoClass,
        IntPtr buffer,
        uint bufferLength,
        out uint returnLength);
    bool SetInformationJobObject(
        IntPtr jobHandle,
        int jobObjectInfoClass,
        IntPtr buffer,
        uint bufferLength);
    bool DuplicateHandleToProcess(
        IntPtr sourceProcessHandle,
        IntPtr sourceHandle,
        IntPtr targetProcessHandle,
        uint desiredAccess,
        out IntPtr duplicatedTargetHandle);
    int GetSecurityInfo(
        IntPtr handle,
        FileSecurityNative.SE_OBJECT_TYPE objectType,
        FileSecurityNative.SECURITY_INFORMATION securityInformation,
        out IntPtr ownerSid,
        out IntPtr groupSid,
        out IntPtr dacl,
        out IntPtr sacl,
        out IntPtr securityDescriptor);
    bool ConvertSecurityDescriptorToStringSecurityDescriptor(
        IntPtr securityDescriptor,
        uint revision,
        FileSecurityNative.SECURITY_INFORMATION securityInformation,
        out IntPtr stringSecurityDescriptor,
        out uint stringSecurityDescriptorLength);
    IntPtr LocalFree(IntPtr memory);
    void CloseHandle(IntPtr handle);
    int GetLastWin32Error();
    int GetProcessId(IntPtr processHandle);
}
