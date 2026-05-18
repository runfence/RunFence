namespace RunFence.Security;

public interface ISecureDesktopNative
{
    SecureDesktopNativeResult CaptureOriginalDesktop();
    SecureDesktopNativeResult CreateSecureDesktop(string name);
    SecureDesktopNativeResult SwitchDesktop(IntPtr desktopHandle);
    SecureDesktopNativeResult RestoreDesktop(IntPtr desktopHandle, string? originalDesktopIdentity);
    SecureDesktopNativeResult CloseDesktop(IntPtr desktopHandle);
    string FormatNativeError(int? nativeErrorCode);
}

