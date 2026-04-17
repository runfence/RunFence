using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using RunFence.Infrastructure;

namespace RunFence.Security;

public class SecureDesktopHelper : ISecureDesktopRunner
{
    private const uint DESKTOP_ALL = 0x01FF;
    private const int UOI_NAME = 2;
    private const uint WM_CLOSE = 0x0010;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktopW(string desk, IntPtr dev, IntPtr dm, uint flags, uint access, IntPtr sa);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SwitchDesktop(IntPtr hDesk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex,
        [MarshalAs(UnmanagedType.LPWStr)] StringBuilder pvInfo, int nLength, out int lpnLengthNeeded);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumDesktopWindows(IntPtr hDesktop, EnumDesktopWindowsProc lpfn, IntPtr lParam);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
        string sddl, uint revision, out IntPtr sd, out uint size);

    private delegate bool EnumDesktopWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public bool bInheritHandle;
    }

    private static int _active;
    [ThreadStatic] private static bool _inDoEvents;

    void ISecureDesktopRunner.Run(Action action) => RunSecureDesktop(action);

    private void RunSecureDesktop(Action action)
    {
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
        {
            action(); // fallback: run on current desktop (reentrancy)
            return;
        }

        IntPtr origDesk = OpenInputDesktop(0, false, DESKTOP_ALL);
        if (origDesk == IntPtr.Zero)
        {
            Interlocked.Exchange(ref _active, 0);
            action();
            return;
        }

        string deskName = "D_" + Guid.NewGuid().ToString("N");
        IntPtr newDesk = CreateRestrictedDesktop(deskName);
        if (newDesk == IntPtr.Zero)
        {
            Interlocked.Exchange(ref _active, 0);
            CloseDesktop(origDesk);
            action();
            return;
        }

        try
        {
            if (!SwitchDesktop(newDesk))
            {
                action();
                return;
            }

            Exception? threadException = null;
            var thread = new Thread(() =>
            {
                if (!SetThreadDesktop(newDesk))
                {
                    // Fallback: run on this thread without desktop association
                    action();
                    return;
                }

                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    threadException = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            // Pump messages while waiting so IPC InvokeOnUIThread doesn't deadlock.
            // If the input desktop switches away (e.g. Ctrl+Alt+Del → lock/switch user),
            // post WM_CLOSE to all windows on the secure desktop so ShowDialog() returns.
            // _inDoEvents guards against re-entrant DoEvents calls from event handlers
            // triggered during message pumping (e.g. a nested dialog open).
            while (thread.IsAlive)
            {
                if (!_inDoEvents)
                {
                    _inDoEvents = true;
                    try { Application.DoEvents(); }
                    finally { _inDoEvents = false; }
                }
                thread.Join(50);

                IntPtr inputDesk = OpenInputDesktop(0, false, DESKTOP_ALL);
                if (inputDesk != IntPtr.Zero)
                {
                    string? inputName = GetDesktopName(inputDesk);
                    CloseDesktop(inputDesk);
                    if (inputName != null && !string.Equals(inputName, deskName, StringComparison.Ordinal))
                        CloseAllWindowsOnDesktop(newDesk);
                }
            }

            if (threadException != null)
                ExceptionDispatchInfo.Capture(threadException).Throw();
        }
        finally
        {
            Interlocked.Exchange(ref _active, 0);
            SwitchDesktop(origDesk);
            CloseDesktop(newDesk);
            CloseDesktop(origDesk);
        }
    }

    private static string? GetDesktopName(IntPtr hDesk)
    {
        var sb = new StringBuilder(256);
        return GetUserObjectInformation(hDesk, UOI_NAME, sb, sb.Capacity * 2, out _) ? sb.ToString() : null;
    }

    private static void CloseAllWindowsOnDesktop(IntPtr hDesktop)
    {
        EnumDesktopWindows(hDesktop, (hwnd, _) =>
        {
            WindowNative.PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
    }

    private static IntPtr CreateRestrictedDesktop(string name)
    {
        string userSid;
        using (var id = WindowsIdentity.GetCurrent())
            userSid = id.User!.Value;

        string sddl = $"D:(A;;GA;;;{userSid})";
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, 1, out IntPtr sd, out _))
            return IntPtr.Zero;

        IntPtr saPtr = IntPtr.Zero;
        try
        {
            var sa = new SECURITY_ATTRIBUTES
            {
                nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
                lpSecurityDescriptor = sd,
                bInheritHandle = false
            };
            saPtr = Marshal.AllocHGlobal(Marshal.SizeOf<SECURITY_ATTRIBUTES>());
            Marshal.StructureToPtr(sa, saPtr, false);
            return CreateDesktopW(name, IntPtr.Zero, IntPtr.Zero, 0, DESKTOP_ALL, saPtr);
        }
        finally
        {
            if (saPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(saPtr);
            ProcessNative.LocalFree(sd);
        }
    }
}
