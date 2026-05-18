using RunFence.Core;

namespace RunFence.RunAs;

public sealed class RunFenceLauncherPathProvider : IRunFenceLauncherPathProvider
{
    public string GetLauncherPath() => Path.Combine(AppContext.BaseDirectory, PathConstants.LauncherExeName);

    public bool Exists() => File.Exists(GetLauncherPath());
}
