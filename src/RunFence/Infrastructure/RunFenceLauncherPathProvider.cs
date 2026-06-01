using RunFence.Core;

namespace RunFence.Infrastructure;

public sealed class RunFenceLauncherPathProvider : IRunFenceLauncherPathProvider
{
    public string GetLauncherPath() => Path.Combine(AppContext.BaseDirectory, PathConstants.LauncherExeName);

    public bool Exists() => File.Exists(GetLauncherPath());
}
