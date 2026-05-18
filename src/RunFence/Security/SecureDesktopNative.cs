using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using RunFence.Infrastructure;

namespace RunFence.Security;

public class SecureDesktopNative : ISecureDesktopNative
{
    private const uint DesktopAll = 0x01FF;
    private const int UoiName = 2;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktopW(string desk, IntPtr dev, IntPtr dm, uint flags, uint access, IntPtr sa);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll", EntryPoint = "SwitchDesktop", SetLastError = true)]
    private static extern bool SwitchDesktopNative(IntPtr hDesk);
    [DllImport("user32.dll", EntryPoint = "CloseDesktop", SetLastError = true)]
    private static extern bool CloseDesktopNative(IntPtr hDesk);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder pvInfo, int nLength, out int lpnLengthNeeded);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, uint revision, out IntPtr sd, out uint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public bool bInheritHandle;
    }

    public SecureDesktopNativeResult CaptureOriginalDesktop()
    {
        var handle = OpenInputDesktop(0, false, DesktopAll);
        if (handle == IntPtr.Zero)
            return FailureFromLastError();

        return new SecureDesktopNativeResult(
            SecureDesktopNativeStatus.Succeeded,
            handle,
            GetDesktopName(handle),
            null,
            null);
    }

    public SecureDesktopNativeResult CreateSecureDesktop(string name)
    {
        var handle = CreateRestrictedDesktop(name);
        if (handle == IntPtr.Zero)
            return FailureFromLastError();

        return new SecureDesktopNativeResult(SecureDesktopNativeStatus.Succeeded, handle, null, null, null);
    }

    public SecureDesktopNativeResult SwitchDesktop(IntPtr desktopHandle)
    {
        if (SwitchDesktopNative(desktopHandle))
            return new SecureDesktopNativeResult(SecureDesktopNativeStatus.Succeeded, desktopHandle, null, null, null);
        return FailureFromLastError(desktopHandle);
    }

    public SecureDesktopNativeResult RestoreDesktop(IntPtr desktopHandle, string? originalDesktopIdentity)
    {
        if (desktopHandle == IntPtr.Zero)
            return new SecureDesktopNativeResult(SecureDesktopNativeStatus.Unavailable, IntPtr.Zero, originalDesktopIdentity, null, null);

        if (SwitchDesktopNative(desktopHandle))
            return new SecureDesktopNativeResult(SecureDesktopNativeStatus.Succeeded, desktopHandle, originalDesktopIdentity, null, null);

        return FailureFromLastError(desktopHandle, originalDesktopIdentity);
    }

    public SecureDesktopNativeResult CloseDesktop(IntPtr desktopHandle)
    {
        if (desktopHandle == IntPtr.Zero)
            return new SecureDesktopNativeResult(SecureDesktopNativeStatus.Succeeded, IntPtr.Zero, null, null, null);

        if (CloseDesktopNative(desktopHandle))
            return new SecureDesktopNativeResult(SecureDesktopNativeStatus.Succeeded, IntPtr.Zero, null, null, null);

        return FailureFromLastError(desktopHandle);
    }

    public string FormatNativeError(int? nativeErrorCode)
    {
        if (nativeErrorCode is null)
            return "Native error unavailable.";
        return $"Native error {nativeErrorCode.Value}.";
    }

    private static string? GetDesktopName(IntPtr desktop)
    {
        var sb = new StringBuilder(256);
        return GetUserObjectInformation(desktop, UoiName, sb, sb.Capacity * 2, out _) ? sb.ToString() : null;
    }

    private static IntPtr CreateRestrictedDesktop(string name)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var userSid = identity.User!.Value;
        var sddl = $"D:(A;;GA;;;{userSid})";
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, 1, out var sd, out _))
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
            return CreateDesktopW(name, IntPtr.Zero, IntPtr.Zero, 0, DesktopAll, saPtr);
        }
        finally
        {
            if (saPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(saPtr);
            ProcessNative.LocalFree(sd);
        }
    }

    private static SecureDesktopNativeResult FailureFromLastError(IntPtr openedHandle = default, string? originalDesktopIdentity = null)
    {
        var error = Marshal.GetLastWin32Error();
        var status = error == 5 ? SecureDesktopNativeStatus.AccessDenied : SecureDesktopNativeStatus.Failed;
        return new SecureDesktopNativeResult(status, openedHandle, originalDesktopIdentity, error, null);
    }
}
