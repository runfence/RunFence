using RunFence.ForegroundMarker;
using RunFence.Infrastructure;
using RunFence.Core;

namespace RunFence.Startup;

public sealed class ForegroundPrivilegeMarkerStartupEventWirer(
    IStartupIpcHost startupIpcHost,
    IAppStateProvider appStateProvider,
    IForegroundPrivilegeMarkerService foregroundPrivilegeMarkerService,
    ILoggingService log,
    ITrayWarningSink trayWarningSink) : IStartupEventWirer
{
    private bool wired;
    private bool started;
    private const string StartFailureWarningText =
        "Foreground privilege marker failed to start. It has been disabled for this RunFence session.";

    public void WireEvents()
    {
        if (wired || started)
            return;

        startupIpcHost.HandleCreated += OnHandleCreated;
        wired = true;
    }

    private void OnHandleCreated(object? sender, EventArgs e)
    {
        if (started)
            return;

        var settings = appStateProvider.Database.Settings;
        var markerWindowEnabled = settings.ShowForegroundPrivilegeMarker;
        var markerWindowEnabledWhenFullscreen = settings.ShowForegroundPrivilegeMarkerWhenFullscreen;
        try
        {
            foregroundPrivilegeMarkerService.Start(markerWindowEnabled, markerWindowEnabledWhenFullscreen);
        }
        catch (Exception ex)
        {
            LogWarning($"Foreground privilege marker startup failed: {ex.Message}");

            try
            {
                foregroundPrivilegeMarkerService.Stop();
            }
            catch (Exception stopEx)
            {
                LogWarning($"Failed to stop foreground privilege marker after startup failure: {stopEx.Message}");
            }

            try
            {
                trayWarningSink.ShowWarning(StartFailureWarningText);
            }
            catch (Exception warningEx)
            {
                LogWarning($"Failed to show foreground privilege marker startup warning: {warningEx.Message}");
            }
        }
        finally
        {
            started = true;
            wired = false;
            startupIpcHost.HandleCreated -= OnHandleCreated;
        }
    }

    private void LogWarning(string message)
    {
        try
        {
            log.Warn(message);
        }
        catch
        {
        }
    }
}
