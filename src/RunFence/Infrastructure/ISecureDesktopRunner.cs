namespace RunFence.Infrastructure;

/// <summary>
/// Runs an action on a restricted secure desktop, isolating it from other processes and input.
/// </summary>
public interface ISecureDesktopRunner
{
    void Run(Action action);
}
