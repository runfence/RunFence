namespace RunFence.AppxLauncher;

public interface IWinRtStaDispatcher
{
    void WaitForDispatch(DateTime deadlineUtc);
}

public sealed class WinRtStaDispatcher : IWinRtStaDispatcher
{
    public void WaitForDispatch(DateTime deadlineUtc)
    {
        var remaining = deadlineUtc - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
            return;

        var waitMilliseconds = Math.Min((uint)Math.Ceiling(remaining.TotalMilliseconds), 25u);
        _ = WinRtNative.MsgWaitForMultipleObjectsEx(
            0,
            IntPtr.Zero,
            waitMilliseconds,
            WinRtNative.QsAllInput,
            WinRtNative.MwmoInputAvailable);
        PumpWindowMessages();
    }

    private void PumpWindowMessages()
    {
        while (WinRtNative.PeekMessage(out var message, IntPtr.Zero, 0, 0, WinRtNative.PmRemove))
        {
            _ = WinRtNative.TranslateMessage(ref message);
            _ = WinRtNative.DispatchMessage(ref message);
        }
    }
}
