using System.Diagnostics;

namespace RunFence.Infrastructure;

public class SystemStopwatchProvider : IStopwatchProvider
{
    public long GetTimestamp() => Stopwatch.GetTimestamp();

    public double GetElapsedSeconds(long startTimestamp, long endTimestamp) =>
        Stopwatch.GetElapsedTime(startTimestamp, endTimestamp).TotalSeconds;
}