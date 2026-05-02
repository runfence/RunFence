using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Launch;

public class AppEntryLauncher(
    ILaunchFacade launchFacade,
    IAppEntryLaunchPlanBuilder planBuilder,
    ISessionProvider sessionProvider)
    : IAppEntryLauncher
{
    public void Launch(AppEntry app, string? launcherArguments, string? launcherWorkingDirectory = null,
        Func<string, string, bool>? permissionPrompt = null, string? associationArgsTemplate = null)
    {
        var session = sessionProvider.GetSession();
        var plan = planBuilder.Build(app, session, launcherArguments, launcherWorkingDirectory, associationArgsTemplate);

        switch (plan.Kind)
        {
            case AppEntryLaunchKind.Url:
                launchFacade.LaunchUrl(plan.Url!, plan.Identity);
                return;
            case AppEntryLaunchKind.Folder:
                launchFacade.LaunchFolderBrowser(plan.Identity, plan.FolderPath, folderPermissionPrompt: permissionPrompt);
                return;
            case AppEntryLaunchKind.File:
                launchFacade.LaunchFile(plan.FileTarget!, plan.Identity, permissionPrompt: permissionPrompt);
                return;
            default:
                throw new InvalidOperationException($"Unsupported launch plan kind '{plan.Kind}'.");
        }
    }
}
