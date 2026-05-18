using RunFence.Core.Models;

namespace RunFence.Launch;

public class AppEntryLaunchPlanBuilder : IAppEntryLaunchPlanBuilder
{
    public AppEntryLaunchPlan Build(
        AppEntry app,
        SessionContext session,
        string? launcherArguments,
        string? launcherWorkingDirectory = null,
        string? associationArgsTemplate = null)
    {
        LaunchIdentity identity;
        if (app.AppContainerName != null)
        {
            var entry = session.Database.AppContainers
                            .FirstOrDefault(c => string.Equals(c.Name, app.AppContainerName, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException(
                            $"AppContainer '{app.AppContainerName}' not found in database.");

            identity = new AppContainerLaunchIdentity(entry);
        }
        else
        {
            identity = new AccountLaunchIdentity(app.AccountSid)
            {
                PrivilegeLevel = app.PrivilegeLevel,
            };
        }

        if (app.IsUrlScheme)
            return new AppEntryLaunchPlan(AppEntryLaunchKind.Url, identity, Url: app.ExePath);

        if (app.IsFolder)
            return new AppEntryLaunchPlan(AppEntryLaunchKind.Folder, identity, FolderPath: app.ExePath);

        var workingDirectory = ProcessLaunchHelper.DetermineWorkingDirectory(app, launcherWorkingDirectory)
                               ?? (app.AppContainerName != null
                                   ? Path.GetDirectoryName(Path.GetFullPath(app.ExePath)) ?? ""
                                   : null);
        var target = new ProcessLaunchTarget(
            ExePath: app.ExePath,
            WorkingDirectory: workingDirectory,
            Arguments: ProcessLaunchHelper.DetermineArguments(app, launcherArguments, associationArgsTemplate),
            EnvironmentVariables: app.EnvironmentVariables,
            IsPathApproved: associationArgsTemplate == null);
        return new AppEntryLaunchPlan(AppEntryLaunchKind.File, identity, FileTarget: target);
    }
}
