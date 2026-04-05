namespace RunFence.Infrastructure;

public interface IStopwatchProvider
{
    long GetTimestamp();
    double GetElapsedSeconds(long startTimestamp, long endTimestamp);
}