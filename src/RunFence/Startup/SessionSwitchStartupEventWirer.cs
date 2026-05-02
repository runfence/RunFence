using Microsoft.Win32;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Startup;

public class SessionSwitchStartupEventWirer(
    ISessionSwitchEventSource sessionSwitchEventSource,
    IStartupFormLifetime formLifetime,
    IInteractiveUserDesktopProvider interactiveUserDesktopProvider) : IStartupEventWirer
{
    public void WireEvents()
    {
        sessionSwitchEventSource.SessionSwitch += OnSessionSwitch;
        formLifetime.FormClosed += (_, _) => sessionSwitchEventSource.SessionSwitch -= OnSessionSwitch;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is not (SessionSwitchReason.ConsoleConnect
            or SessionSwitchReason.ConsoleDisconnect
            or SessionSwitchReason.SessionLogon
            or SessionSwitchReason.SessionLogoff
            or SessionSwitchReason.RemoteConnect
            or SessionSwitchReason.RemoteDisconnect))
            return;

        SidResolutionHelper.ReinitializeInteractiveUserSid();
        interactiveUserDesktopProvider.InvalidateCache();
    }
}
