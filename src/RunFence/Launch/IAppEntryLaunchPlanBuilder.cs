using RunFence.Core.Models;

namespace RunFence.Launch;

public interface IAppEntryLaunchPlanBuilder
{
    AppEntryLaunchPlan Build(
        AppEntry app,
        SessionContext session,
        string? launcherArguments,
        string? launcherWorkingDirectory = null,
        string? associationArgsTemplate = null);
}
