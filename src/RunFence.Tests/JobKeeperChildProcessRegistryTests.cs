using RunFence.JobKeeper;
using Xunit;

namespace RunFence.Tests;

public sealed class JobKeeperChildProcessRegistryTests
{
    [Fact]
    public void Register_PrunesExitedHandlesBeforeAddingNewHandle()
    {
        var nativeApi = new FakeNativeProcessApi();
        nativeApi.SetExited(new IntPtr(10), exited: true);
        nativeApi.SetExited(new IntPtr(20), exited: false);
        var registry = new JobKeeperChildProcessRegistry(nativeApi);

        registry.Register(new IntPtr(10));
        registry.Register(new IntPtr(20));

        Assert.Equal(1, registry.PruneExitedAndCountActive());
        Assert.Equal([new IntPtr(10)], nativeApi.ClosedHandles);
    }

    [Fact]
    public void PruneExitedAndCountActive_RemovesExitedHandlesAndKeepsActiveHandles()
    {
        var nativeApi = new FakeNativeProcessApi();
        nativeApi.SetImagePath(new IntPtr(10), @"C:\Apps\app.exe");
        nativeApi.SetImagePath(new IntPtr(20), @"C:\Apps\other.exe");
        nativeApi.SetImagePath(new IntPtr(30), @"C:\Apps\third.exe");
        nativeApi.SetExited(new IntPtr(10), exited: false);
        nativeApi.SetExited(new IntPtr(20), exited: true);
        nativeApi.SetExited(new IntPtr(30), exited: true);
        var registry = new JobKeeperChildProcessRegistry(nativeApi);

        registry.Register(new IntPtr(10));
        registry.Register(new IntPtr(20));
        registry.Register(new IntPtr(30));

        var activeCount = registry.PruneExitedAndCountActive();

        Assert.Equal(1, activeCount);
        Assert.Equal([new IntPtr(20), new IntPtr(30)], nativeApi.ClosedHandles.OrderBy(handle => handle.ToInt64()).ToArray());
    }

    [Fact]
    public void PruneExitedAndCountActive_WhenOnlyIgnoredLingeringProcessRemains_TreatsItAsNoActiveProcesses()
    {
        var nativeApi = new FakeNativeProcessApi();
        nativeApi.SetImagePath(new IntPtr(10), Path.Combine(Environment.SystemDirectory, "conhost.exe"));
        nativeApi.SetExited(new IntPtr(10), exited: false);
        var registry = new JobKeeperChildProcessRegistry(nativeApi);

        registry.Register(new IntPtr(10));

        Assert.Equal(0, registry.PruneExitedAndCountActive());
    }

    [Fact]
    public void TryExitAfterCleaningIgnoredProcesses_WhenOnlyIgnoredLingeringProcessRemains_TerminatesItAndExits()
    {
        var nativeApi = new FakeNativeProcessApi();
        nativeApi.SetImagePath(new IntPtr(10), Path.Combine(Environment.SystemDirectory, "conhost.exe"));
        nativeApi.SetExited(new IntPtr(10), exited: false);
        var registry = new JobKeeperChildProcessRegistry(nativeApi);

        registry.Register(new IntPtr(10));

        Assert.True(registry.TryExitAfterCleaningIgnoredProcesses());
        Assert.Equal([new IntPtr(10)], nativeApi.TerminatedHandles);
    }

    [Fact]
    public void TryExitAfterCleaningIgnoredProcesses_WhenIgnoredLingeringProcessStillRemainsAfterKill_DoesNotExit()
    {
        var nativeApi = new FakeNativeProcessApi { IgnoreTerminationExitState = true };
        nativeApi.SetImagePath(new IntPtr(10), Path.Combine(Environment.SystemDirectory, "conhost.exe"));
        nativeApi.SetExited(new IntPtr(10), exited: false);
        var registry = new JobKeeperChildProcessRegistry(nativeApi);

        registry.Register(new IntPtr(10));

        Assert.False(registry.TryExitAfterCleaningIgnoredProcesses());
        Assert.Equal([new IntPtr(10)], nativeApi.TerminatedHandles);
    }

    private sealed class FakeNativeProcessApi : IJobKeeperNativeProcessApi
    {
        private readonly Dictionary<IntPtr, bool> _exitStates = [];
        private readonly Dictionary<IntPtr, string?> _imagePaths = [];

        public List<IntPtr> ClosedHandles { get; } = [];
        public List<IntPtr> TerminatedHandles { get; } = [];
        public bool IgnoreTerminationExitState { get; init; }

        public void SetExited(IntPtr processHandle, bool exited)
        {
            _exitStates[processHandle] = exited;
        }

        public void SetImagePath(IntPtr processHandle, string? imagePath)
        {
            _imagePaths[processHandle] = imagePath;
        }

        public bool CreateProcess(
            string? applicationName,
            System.Text.StringBuilder commandLine,
            uint creationFlags,
            IntPtr environmentBlock,
            string? workingDirectory,
            bool hideWindow,
            bool suppressStartupFeedback,
            out JobKeeperProcessInformation processInformation)
        {
            processInformation = default;
            throw new NotSupportedException();
        }

        public void CloseHandle(IntPtr handle)
        {
            ClosedHandles.Add(handle);
        }

        public int GetLastWin32Error() => 0;

        public bool TerminateProcess(IntPtr processHandle, uint exitCode)
        {
            TerminatedHandles.Add(processHandle);
            if (!IgnoreTerminationExitState)
                _exitStates[processHandle] = true;
            return true;
        }

        public string? TryGetProcessImagePath(IntPtr processHandle) =>
            _imagePaths.TryGetValue(processHandle, out var imagePath) ? imagePath : null;

        public bool WaitForProcessExit(IntPtr processHandle, uint timeoutMilliseconds) =>
            _exitStates.TryGetValue(processHandle, out var exited) && exited;
    }
}
