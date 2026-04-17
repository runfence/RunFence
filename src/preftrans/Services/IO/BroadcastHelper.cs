using PrefTrans.Native;

namespace PrefTrans.Services.IO;

public class BroadcastHelper : IBroadcastHelper
{
    public void Broadcast()
    {
        NativeMethods.SendMessageTimeoutW(
            Constants.HWND_BROADCAST, Constants.WM_SETTINGCHANGE,
            UIntPtr.Zero, null,
            Constants.SMTO_ABORTIFHUNG, 1000, out _);
    }

    public void BroadcastEnvironment()
    {
        NativeMethods.SendMessageTimeoutW(
            Constants.HWND_BROADCAST, Constants.WM_SETTINGCHANGE,
            UIntPtr.Zero, "Environment",
            Constants.SMTO_ABORTIFHUNG, 1000, out _);
    }

    public void BroadcastIntl()
    {
        NativeMethods.SendMessageTimeoutW(
            Constants.HWND_BROADCAST, Constants.WM_SETTINGCHANGE,
            UIntPtr.Zero, "intl",
            Constants.SMTO_ABORTIFHUNG, 1000, out _);
    }
}