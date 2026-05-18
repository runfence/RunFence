namespace RunFence.Launcher;

public interface ILauncherAssociationFallbackService
{
    int LaunchFallback(string association, string? rawArguments);
    int CleanupAndLaunchFallback(string association, string? rawArguments);
}
