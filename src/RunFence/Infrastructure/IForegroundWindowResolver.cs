namespace RunFence.Infrastructure;

public interface IForegroundWindowResolver
{
    ForegroundWindowInfo GetForegroundWindow();
}
