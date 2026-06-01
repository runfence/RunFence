using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;

namespace RunFence.Apps;

public sealed class RepairingAppEntryLauncher(
    ISessionProvider sessionProvider,
    UiThreadAppEntryPathRepairService appEntryPathRepairService,
    IAppEntryLaunchExecutor launchExecutor)
    : IAppEntryLauncher
{
    public LaunchExecutionResult Launch(AppEntry app, string? launcherArguments, string? launcherWorkingDirectory = null,
        Func<string, string, bool>? permissionPrompt = null, string? associationArgsTemplate = null)
    {
        var launchApp = app;
        if (!app.IsUrlScheme && !string.IsNullOrWhiteSpace(app.Id))
        {
            var session = sessionProvider.GetSession();
            var liveApp = session.Database.Apps.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, app.Id, StringComparison.OrdinalIgnoreCase));
            if (liveApp != null)
                launchApp = appEntryPathRepairService.RepairIfNeeded(liveApp.Id).App;
        }

        return launchExecutor.Launch(
            launchApp,
            launcherArguments,
            launcherWorkingDirectory,
            permissionPrompt,
            associationArgsTemplate);
    }
}
