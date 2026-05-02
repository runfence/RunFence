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
    public void IsBlocked_OneDecline_ImmediatelyAfterDecline_ReturnsFalse()
    {
        var dos = CreateProtection();

        _currentTimestamp = 100;
        dos.RecordDecline();

        Assert.False(dos.IsBlocked());
    }

    [Fact]
    public void IsBlocked_ThreeDeclinesWithinWindow_ReturnsFalse()
    {
        var dos = CreateProtection();

        _currentTimestamp = 100;
        dos.RecordDecline();
        _currentTimestamp = 101;
        dos.RecordDecline();
        _currentTimestamp = 102;
        dos.RecordDecline();

        Assert.False(dos.IsBlocked());
    }

    [Fact]
    public void IsBlocked_FourDeclines_Within2min_ReturnsTrue()
    {
        var dos = CreateProtection();

        // Record 4 declines spread across time but within 2 min window
        _currentTimestamp = 0;
        dos.RecordDecline();
        _currentTimestamp = 20;
        dos.RecordDecline();
        _currentTimestamp = 40;
        dos.RecordDecline();
        _currentTimestamp = 60;
        dos.RecordDecline();

        _currentTimestamp = 61;
        Assert.True(dos.IsBlocked());
    }

    [Fact]
    public void IsBlocked_FourDeclines_After2min_ReturnsFalse()
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

        _currentTimestamp = 180;
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

        _currentTimestamp = 61;
        Assert.True(dos.IsBlocked()); // 4 declines in 2 min

        // Advance past window (>120s from first decline at t=0)
        _currentTimestamp = 180;
        dos.RecordDecline(); // 5th decline — window resets, count=1
        Assert.False(dos.IsBlocked()); // only 1 decline in new window
    }

    [Fact]
    public void Reset_ClearsAllState()
    {
        var dos = CreateProtection();
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
