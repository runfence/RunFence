using System.Security.Principal;
using RunFence.Core;
using RunFence.Startup.UI;
#pragma warning disable CS0162 // Unreachable code detected

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
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator) && !DebugHelper.IsDebugBuild)
        {
            ui.ShowError("RunFence must be run as Administrator.", "Elevation Required");
            return false;
        }

        return true;
    }
}