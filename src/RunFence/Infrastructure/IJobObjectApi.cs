namespace RunFence.Infrastructure;

public interface IJobObjectApi
{
    IntPtr CreateJobObject(string? name, string? securityDescriptorSddl);
    IntPtr OpenJobObject(uint desiredAccess, bool inheritHandle, string name);
    bool AssignProcessToJobObject(IntPtr jobHandle, IntPtr processHandle);
    bool? IsProcessInJob(IntPtr processHandle, IntPtr jobHandle);
    bool AreSameJobObject(IntPtr firstJobHandle, IntPtr secondJobHandle);
    int GetProcessId(IntPtr processHandle);
    bool SetUiRestrictions(IntPtr jobHandle, uint flags);
    uint? QueryUiRestrictions(IntPtr jobHandle);
    uint? QueryBasicLimitFlags(IntPtr jobHandle);
    HashSet<int>? QueryProcessIds(IntPtr jobHandle);
    bool DuplicateHandleToProcess(
        IntPtr sourceProcessHandle,
        IntPtr sourceHandle,
        IntPtr targetProcessHandle,
        uint desiredAccess)
    {
        return DuplicateHandleToProcess(
            sourceProcessHandle,
            sourceHandle,
            targetProcessHandle,
            desiredAccess,
            out _);
    }
    bool DuplicateHandleToProcess(
        IntPtr sourceProcessHandle,
        IntPtr sourceHandle,
        IntPtr targetProcessHandle,
        uint desiredAccess,
        out IntPtr duplicatedTargetHandle);
    JobObjectSecuritySnapshot? GetSecuritySnapshot(IntPtr jobHandle);
    void CloseHandle(IntPtr handle);
    int GetLastWin32Error();
}
