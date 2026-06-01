namespace RunFence.Infrastructure;

public sealed class JobKeeperProcessHandleOpener : IJobKeeperProcessHandleOpener
{
    public IntPtr OpenForDuplicate(int pid) =>
        ProcessNative.OpenProcess(
            ProcessNative.ProcessDuplicateHandle | ProcessNative.ProcessQueryLimitedInformation,
            false,
            pid);

    public void Close(IntPtr handle) => ProcessNative.CloseHandle(handle);
}
