namespace RunFence.Infrastructure;

public interface IRunFenceLauncherPathProvider
{
    string GetLauncherPath();

    bool Exists();
}
