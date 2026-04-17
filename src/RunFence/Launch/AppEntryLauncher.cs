using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Launch;

public class AppEntryLauncher(
    ILaunchFacade launchFacade,
    ISessionProvider sessionProvider)
    : IAppEntryLauncher
{
    public void Launch(AppEntry app, string? launcherArguments, string? launcherWorkingDirectory = null,
        Func<string, string, bool>? permissionPrompt = null, string? associationArgsTemplate = null)
    {
        var session = sessionProvider.GetSession();

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
        {
            launchFacade.LaunchUrl(app.ExePath, identity);
            return;
        }

        if (app.IsFolder)
        {
            launchFacade.LaunchFolderBrowser(identity, app.ExePath, folderPermissionPrompt: permissionPrompt);
            return;
        }

        var workingDirectory = ProcessLaunchHelper.DetermineWorkingDirectory(app, launcherWorkingDirectory)
                               ?? (app.AppContainerName != null
                                   ? Path.GetDirectoryName(Path.GetFullPath(app.ExePath)) ?? ""
                                   : null);
        launchFacade.LaunchFile(new ProcessLaunchTarget(
            ExePath: app.ExePath,
            WorkingDirectory: workingDirectory,
            Arguments: ProcessLaunchHelper.DetermineArguments(app, launcherArguments, associationArgsTemplate),
            EnvironmentVariables: app.EnvironmentVariables), identity, permissionPrompt: permissionPrompt);
    }
}
