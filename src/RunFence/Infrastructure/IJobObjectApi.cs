namespace RunFence.Infrastructure;

public interface IJobObjectApi
{
    IntPtr CreateJobObject(string? name, string? securityDescriptorSddl);
    IntPtr OpenJobObject(uint desiredAccess, bool inheritHandle, string name);
    bool AssignProcessToJobObject(IntPtr jobHandle, IntPtr processHandle);
    int GetProcessId(IntPtr processHandle);
    bool SetInformationJobObject(IntPtr jobHandle, int infoClass, IntPtr info, uint infoLength);
    bool QueryInformationJobObject(IntPtr jobHandle, int infoClass, IntPtr info, uint infoLength, out uint returnLength);
    bool SetUiRestrictions(IntPtr jobHandle, uint flags);
    uint? QueryUiRestrictions(IntPtr jobHandle);
    HashSet<int>? QueryProcessIds(IntPtr jobHandle);
    bool DuplicateHandleToProcess(
        IntPtr sourceProcessHandle,
        IntPtr sourceHandle,
        IntPtr targetProcessHandle,
        uint desiredAccess);
    JobObjectSecuritySnapshot? GetSecuritySnapshot(IntPtr jobHandle);
    void CloseHandle(IntPtr handle);
    int GetLastWin32Error();
}
