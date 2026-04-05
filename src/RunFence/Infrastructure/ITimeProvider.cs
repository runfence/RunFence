namespace RunFence.Infrastructure;

public interface ITimeProvider
{
    long GetTickCount64();
}