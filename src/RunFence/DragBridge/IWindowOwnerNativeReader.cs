namespace RunFence.DragBridge;

public interface IWindowOwnerNativeReader
{
    bool TryGetForegroundWindow(out IntPtr hwnd, out uint threadId, out uint processId);
    bool TryGetCaptureWindowProcessId(uint foregroundThreadId, IntPtr foregroundHwnd, out uint processId);
    bool TryGetCursorWindowProcessId(out uint processId);
}
