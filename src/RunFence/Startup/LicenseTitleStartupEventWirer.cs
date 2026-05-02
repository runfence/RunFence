using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Licensing;

namespace RunFence.Startup;

public class LicenseTitleStartupEventWirer(
    IUiThreadInvoker uiThreadInvoker,
    ILicenseService licenseService,
    NotifyIcon notifyIcon) : IStartupEventWirer
{
    public void WireEvents()
    {
        licenseService.LicenseStatusChanged +=
            () => uiThreadInvoker.BeginInvoke(() => notifyIcon.Text = BuildTitle());
    }

    private string BuildTitle()
    {
        var title = licenseService.IsLicensed ? "RunFence" : "RunFence (Evaluation)";
        if (DebugHelper.UseAdminOperationMocks)
            title += " [NON-ELEVATED]";
        if (!string.IsNullOrEmpty(DebugHelper.AppId))
            title += $" [{DebugHelper.AppId}]";
        return title;
    }
}
