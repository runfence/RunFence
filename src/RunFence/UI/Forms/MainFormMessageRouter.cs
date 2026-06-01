namespace RunFence.UI.Forms;

public sealed class MainFormMessageRouter(
    MainFormTrayHandler trayHandler,
    MainFormBackgroundAutoLockCoordinator autoLockCoordinator)
{
    public void HandleWndProc(Message message)
    {
        if (message.Msg == MainForm.TaskbarCreatedMessage)
            trayHandler.RestoreIconVisibility();

        if (message.Msg != MainForm.ActivateAppMessage)
            return;

        if (message.WParam == IntPtr.Zero)
            autoLockCoordinator.HandleAppDeactivated();
        else
            autoLockCoordinator.HandleAppActivated();
    }

    public bool PreFilterMessage(ref Message message)
    {
        const int WM_KEYDOWN = 0x0100;
        const int WM_MOUSEMOVE = 0x0200;
        const int WM_LBUTTONDOWN = 0x0201;
        const int WM_RBUTTONDOWN = 0x0204;
        const int WM_MOUSEWHEEL = 0x020A;

        var msg = message.Msg;
        if (msg is WM_KEYDOWN or WM_MOUSEMOVE or WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MOUSEWHEEL)
            trayHandler.ResetIdleTimer();

        return false;
    }
}
