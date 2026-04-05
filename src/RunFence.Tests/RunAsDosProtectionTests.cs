using RunFence.Infrastructure;
using RunFence.RunAs;
using Xunit;

namespace RunFence.Tests;

public class RunAsDosProtectionTests
{
    private long _currentTimestamp;

    private RunAsDosProtection CreateProtection()
    {
        _currentTimestamp = 0;
        var stopwatch = new StubStopwatchProvider(
            getTimestamp: () => _currentTimestamp,
            getElapsedSeconds: (start, end) => end - start);
        return new RunAsDosProtection(stopwatch);
    }

    private sealed class StubStopwatchProvider(Func<long> getTimestamp, Func<long, long, double> getElapsedSeconds) : IStopwatchProvider
    {
        public long GetTimestamp() => getTimestamp();
        public double GetElapsedSeconds(long startTimestamp, long endTimestamp) => getElapsedSeconds(startTimestamp, endTimestamp);
    }

    [Fact]
    public void IsBlocked_NeverDeclined_ReturnsFalse()
    {
        var dos = CreateProtection();

        Assert.False(dos.IsBlocked());
    }

    [Fact]
    public void IsBlocked_OneDecline_Within15s_ReturnsTrue()
    {
        var dos = CreateProtection();

        _currentTimestamp = 100;
        dos.RecordDecline();

        _currentTimestamp = 110; // 10s after decline
        Assert.True(dos.IsBlocked());
    }

    [Fact]
    public void IsBlocked_OneDecline_After15s_ReturnsFalse()
    {
        var dos = CreateProtection();

        _currentTimestamp = 100;
        dos.RecordDecline();

        _currentTimestamp = 116; // 16s after decline
        Assert.False(dos.IsBlocked());
    }

    [Fact]
    public void IsBlocked_FourDeclines_Within4min_ReturnsTrue()
    {
        var dos = CreateProtection();

        // Record 4 declines spread across time but within 4 min window
        _currentTimestamp = 0;
        dos.RecordDecline();
        _currentTimestamp = 20;
        dos.RecordDecline();
        _currentTimestamp = 40;
        dos.RecordDecline();
        _currentTimestamp = 60;
        dos.RecordDecline();

        // Check at 76s (16s after last decline, so >15s cooldown passed,
        // but still within 4 min window with 4 declines)
        _currentTimestamp = 76;
        Assert.True(dos.IsBlocked());
    }

    [Fact]
    public void IsBlocked_FourDeclines_After4min_ReturnsFalse()
    {
        var dos = CreateProtection();

        _currentTimestamp = 0;
        dos.RecordDecline();
        _currentTimestamp = 20;
        dos.RecordDecline();
        _currentTimestamp = 40;
        dos.RecordDecline();
        _currentTimestamp = 60;
        dos.RecordDecline();

        // Check at 300s (5 min after window start, >15s after last decline)
        _currentTimestamp = 300;
        Assert.False(dos.IsBlocked());
    }

    [Fact]
    public void IsBlocked_WindowExpired_NewDecline_ResetsCount()
    {
        var dos = CreateProtection();

        // Record 4 declines at t=0..60 → blocked
        _currentTimestamp = 0;
        dos.RecordDecline();
        _currentTimestamp = 20;
        dos.RecordDecline();
        _currentTimestamp = 40;
        dos.RecordDecline();
        _currentTimestamp = 60;
        dos.RecordDecline();

        _currentTimestamp = 76; // >15s after last decline, still in window
        Assert.True(dos.IsBlocked()); // 4 declines in 4 min

        // Advance past window (>240s from first decline at t=0)
        _currentTimestamp = 300;
        dos.RecordDecline(); // 5th decline — window resets, count=1

        // 16s after the 5th decline — past 15s cooldown
        _currentTimestamp = 316;
        Assert.False(dos.IsBlocked()); // only 1 decline in new window
    }

    [Fact]
    public void Reset_ClearsAllState()
    {
        var dos = CreateProtection();

        // Record 4 declines to trigger both protection mechanisms
        _currentTimestamp = 0;
        dos.RecordDecline();
        _currentTimestamp = 1;
        dos.RecordDecline();
        _currentTimestamp = 2;
        dos.RecordDecline();
        _currentTimestamp = 3;
        dos.RecordDecline();

        // Verify blocked
        _currentTimestamp = 4;
        Assert.True(dos.IsBlocked());

        // Reset and verify unblocked
        dos.Reset();
        Assert.False(dos.IsBlocked());
    }
}