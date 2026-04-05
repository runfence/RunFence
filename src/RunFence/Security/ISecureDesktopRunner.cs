namespace RunFence.Security;

public interface ISecureDesktopRunner
{
    void Run(Action action);
}