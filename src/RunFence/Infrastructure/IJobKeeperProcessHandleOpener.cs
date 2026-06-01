namespace RunFence.Infrastructure;

public interface IJobKeeperProcessHandleOpener
{
    IntPtr OpenForDuplicate(int pid);
    void Close(IntPtr handle);
}
