using System.Diagnostics;
using System.Security.Principal;
using RunFence.Startup.UI;

namespace RunFence.Startup;

public class ElevationChecker(IStartupUI ui)
{
    /// <summary>
    /// Checks elevation and shows an error if not running as administrator.
    /// Returns true if elevation is confirmed (Main should continue).
    /// </summary>
    public bool CheckElevation()
    {
        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator) && !Debugger.IsAttached)
        {
            ui.ShowError("RunFence must be run as Administrator.", "Elevation Required");
            return false;
        }

        return true;
    }
}