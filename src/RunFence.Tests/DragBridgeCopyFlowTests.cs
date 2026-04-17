using RunFence.DragBridge;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for captured file state management (via <see cref="CapturedFileStore"/>) and
/// arg building (via <see cref="DragBridgeCopyFlow.BuildArgs"/>).
/// </summary>
public class DragBridgeCopyFlowTests
{
    // ── GetCapturedFiles — captured file state ───────────────────────────

    [Fact]
    public void GetCapturedFiles_InitialState_ReturnsNullPathsAndNotExpired()
    {
        var store = new CapturedFileStore();

        var (captured, sourceSid, expired) = store.GetCapturedFiles();

        Assert.Null(captured);
        Assert.Null(sourceSid);
        Assert.False(expired);
    }

    [Fact]
    public void GetCapturedFiles_WithinFiveMinutes_ReturnsCapturedFilesAndSid()
    {
        var store = new CapturedFileStore();
        var files = new List<string> { @"C:\file1.txt", @"C:\file2.txt" };
        store.SetCapturedFiles(files, "S-1-5-21-1-2-3-4");

        var (captured, sourceSid, expired) = store.GetCapturedFiles();

        Assert.Equal(files, captured);
        Assert.Equal("S-1-5-21-1-2-3-4", sourceSid);
        Assert.False(expired);
    }

    [Fact]
    public void GetCapturedFiles_AfterFiveMinutes_ReturnsExpiredAndNullPaths()
    {
        // Simulate time: capture at tick 0, read at tick 6 minutes later
        long fakeTick = 0;
        var store = new CapturedFileStore(() => fakeTick);
        store.SetCapturedFiles([@"C:\file1.txt"], "S-1-5-21-1-2-3-4");
        fakeTick = 6 * 60_000; // advance time by 6 minutes

        var (captured, sourceSid, expired) = store.GetCapturedFiles();

        Assert.Null(captured);
        Assert.Null(sourceSid);
        Assert.True(expired);
    }

    [Fact]
    public void GetCapturedFiles_CalledTwiceAfterExpiry_SecondCallNotExpired()
    {
        // After expiry, state is cleared. A second call should return not-expired (no files, no expiry).
        long fakeTick = 0;
        var store = new CapturedFileStore(() => fakeTick);
        store.SetCapturedFiles(["file.txt"], "S-1-2-3");
        fakeTick = 6 * 60_000; // advance time past expiry

        store.GetCapturedFiles(); // clears state
        var (captured, sourceSid, expired) = store.GetCapturedFiles();

        Assert.Null(captured);
        Assert.Null(sourceSid);
        Assert.False(expired);
    }

    // ── BuildArgs — arg format ────────────────────────────────────────────

    private static List<string> InvokeBuildArgs(string pipeName, Point cursorPos, nint restoreHwnd = 0)
        => DragBridgeCopyFlow.BuildArgs(pipeName, cursorPos, restoreHwnd);

    [Fact]
    public void BuildArgs_ContainsRequiredFlagsAndPid()
    {
        var result = InvokeBuildArgs("TestPipe-abc123", new Point(123, 456));

        Assert.Contains("--pipe", result);
        Assert.Contains("TestPipe-abc123", result);
        Assert.Contains("--x", result);
        Assert.Contains("--y", result);
        Assert.Contains("--runfence-pid", result);
        var pidIdx = result.IndexOf("--runfence-pid");
        Assert.Equal(Environment.ProcessId.ToString(), result[pidIdx + 1]);
        Assert.Contains("--restore-hwnd", result);
    }

    [Fact]
    public void BuildArgs_RestoreHwnd_IsIncludedInArgs()
    {
        var result = InvokeBuildArgs("pipe1", new Point(0, 0), restoreHwnd: 12345);

        var idx = result.IndexOf("--restore-hwnd");
        Assert.True(idx >= 0, "--restore-hwnd flag must be present");
        Assert.Equal("12345", result[idx + 1]);
    }

    [Theory]
    [InlineData("RunFence-DragBridge-unique", 10, 20, "10", "20")]
    [InlineData("pipe1", 0, 0, "0", "0")]
    [InlineData("pipe2", -5, -10, "-5", "-10")]
    public void BuildArgs_PipeNameAndCoordinatesFormattedCorrectly(
        string pipeName, int x, int y, string expectedX, string expectedY)
    {
        var result = InvokeBuildArgs(pipeName, new Point(x, y));

        var pipeIdx = result.IndexOf("--pipe");
        var xIdx = result.IndexOf("--x");
        var yIdx = result.IndexOf("--y");

        Assert.True(pipeIdx >= 0, "--pipe flag must be present");
        Assert.True(xIdx >= 0, "--x flag must be present");
        Assert.True(yIdx >= 0, "--y flag must be present");

        Assert.Equal(pipeName, result[pipeIdx + 1]);
        Assert.Equal(expectedX, result[xIdx + 1]);
        Assert.Equal(expectedY, result[yIdx + 1]);
    }

    [Fact]
    public void BuildArgs_DataLength_MatchesExpectedStructure()
    {
        // BuildArgs always produces exactly 10 elements:
        // --pipe <name> --x <x> --y <y> --runfence-pid <pid> --restore-hwnd <hwnd>
        var result = InvokeBuildArgs("some-pipe", new Point(1, 2), restoreHwnd: 3);

        Assert.Equal(10, result.Count);
    }
}