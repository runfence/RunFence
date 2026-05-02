namespace RunFence.JobKeeper;

public interface IJobKeeperEnvironmentNativeApi
{
    bool OpenCurrentProcessToken(out IntPtr tokenHandle);
    bool CreateEnvironmentBlock(out IntPtr environmentBlock, IntPtr tokenHandle);
    bool DestroyEnvironmentBlock(IntPtr environmentBlock);
    void CloseHandle(IntPtr handle);
}
