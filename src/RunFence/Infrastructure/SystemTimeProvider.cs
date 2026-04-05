namespace RunFence.Infrastructure;

public class SystemTimeProvider : ITimeProvider
{
    public long GetTickCount64() => Environment.TickCount64;
}