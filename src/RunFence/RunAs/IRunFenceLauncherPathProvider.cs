namespace RunFence.RunAs;

public interface IRunFenceLauncherPathProvider
{
    string GetLauncherPath();

    bool Exists();
}
