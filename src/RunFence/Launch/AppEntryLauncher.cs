using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Launch;

public class AppEntryLauncher(
    ILaunchFacade launchFacade,
    IAppEntryLaunchPlanBuilder planBuilder,
    ISessionProvider sessionProvider)
    : IAppEntryLaunchExecutor
{
    public LaunchExecutionResult Launch(AppEntry app, string? launcherArguments, string? launcherWorkingDirectory = null,
        Func<string, string, bool>? permissionPrompt = null, string? associationArgsTemplate = null)
    {
        var session = sessionProvider.GetSession();
        var plan = planBuilder.Build(
            app,
            session,
            launcherArguments,
            launcherWorkingDirectory,
            associationArgsTemplate);

        switch (plan.Kind)
        {
            case AppEntryLaunchKind.Url:
                return launchFacade.LaunchUrl(plan.Url!, plan.Identity);
            case AppEntryLaunchKind.Folder:
                return launchFacade.LaunchFolderBrowser(plan.Identity, plan.FolderPath, folderPermissionPrompt: permissionPrompt);
            case AppEntryLaunchKind.File:
                return launchFacade.LaunchFile(plan.FileTarget!, plan.Identity, permissionPrompt: permissionPrompt);
            default:
                throw new InvalidOperationException($"Unsupported launch plan kind '{plan.Kind}'.");
        }
    }
}
