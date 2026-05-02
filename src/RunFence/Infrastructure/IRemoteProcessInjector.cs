namespace RunFence.Infrastructure;

public interface IRemoteProcessInjector
{
    bool TryInjectClipboardData(int targetProcessId, IntPtr hWnd, IReadOnlyList<ClipboardFormatData> formats);
}
