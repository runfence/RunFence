using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;

namespace RunFence.UI;

public sealed class TrayIdleMonitorCoordinator(
    IIdleMonitorService idleMonitor,
    SessionContext session,
    ILicenseService licenseService,
    IApplicationExitService applicationExitService)
{
    private IMainFormVisibility? _form;

    public void Initialize(IMainFormVisibility form)
    {
        if (_form != null)
            throw new InvalidOperationException("TrayIdleMonitorCoordinator.Initialize can only be called once.");

        _form = form;
    }

    public void Configure()
    {
        idleMonitor.Configure(session.Database.Settings.IdleTimeoutMinutes);
    }

    public void Start()
    {
        if (session.Database.Settings.IdleTimeoutMinutes > 0)
            idleMonitor.Start();
    }

    public void Stop()
    {
        idleMonitor.Stop();
    }

    public void HandleIdleTimeout()
    {
        var form = GetForm();
        if (!form.IsDisposed && licenseService.IsLicensed)
            applicationExitService.Exit();
    }

    private IMainFormVisibility GetForm()
        => _form ?? throw new InvalidOperationException("TrayIdleMonitorCoordinator must be initialized before use.");
}
