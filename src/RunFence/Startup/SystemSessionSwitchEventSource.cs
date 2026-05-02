using Microsoft.Win32;
using RunFence.Infrastructure;

namespace RunFence.Startup;

public class SystemSessionSwitchEventSource : ISessionSwitchEventSource
{
    public event SessionSwitchEventHandler? SessionSwitch
    {
        add => SystemEvents.SessionSwitch += value;
        remove => SystemEvents.SessionSwitch -= value;
    }
}
