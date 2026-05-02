namespace RunFence.Infrastructure;

public interface IWindowProcessIdReader
{
    uint GetWindowProcessId(IntPtr hWnd);
}
