using RunFence.Core.Models;

namespace RunFence.Launch;

public interface IAppEntryLauncher
{
    void Launch(AppEntry app, string? launcherArguments, string? launcherWorkingDirectory = null,
        Func<string, string, bool>? permissionPrompt = null, string? associationArgsTemplate = null);
}
