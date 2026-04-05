namespace RunFence.Infrastructure;

/// <summary>
/// Applies per-HWND security settings to every window created in this process via an
/// EVENT_OBJECT_CREATE WinEvent hook:
/// <list type="bullet">
/// <item>Blocks WM_GETOBJECT — prevents lower-IL processes from using UIAutomation/MSAA to
/// simulate user interaction.</item>
/// <item>Allows OLE drag-drop messages (WM_DROPFILES, WM_COPYDATA, WM_COPYGLOBALDATA) —
/// OLE creates internal helper windows during RegisterDragDrop that also need the filter;
/// applying it here covers all HWNDs including those hidden ones.</item>
/// </list>
/// </summary>
public sealed class WindowSecurityService : IDisposable
{
    private const uint EventObjectCreate = 0x8000;
    private const int ObjidWindow = 0;

    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private readonly NativeInterop.WinEventDelegate _delegate;
    private readonly IntPtr _hook;

    public WindowSecurityService()
    {
        _delegate = OnObjectCreate; // keep delegate alive for the lifetime of the hook
        uint pid = (uint)Environment.ProcessId;
        _hook = NativeInterop.SetWinEventHook(
            EventObjectCreate, EventObjectCreate,
            IntPtr.Zero, _delegate,
            pid, 0,
            NativeInterop.WinEventOutOfContext);
    }

    private static void OnObjectCreate(IntPtr hook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (hwnd == IntPtr.Zero || idObject != ObjidWindow || idChild != 0)
            return;
        NativeMethods.ChangeWindowMessageFilterEx(hwnd, NativeMethods.WM_GETOBJECT,
            NativeMethods.MSGFLT_DISALLOW, IntPtr.Zero);
        NativeMethods.AllowDropFromLowIL(hwnd);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
            NativeInterop.UnhookWinEvent(_hook);
    }
}