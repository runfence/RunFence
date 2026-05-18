using RunFence.Core.Ipc;
using RunFence.Launcher;
using Xunit;

namespace RunFence.Tests;

public class LauncherIpcHelperTests
{
    [Fact]
    public void ExistingGuiWaitsForIpcReadinessBeforeSendingMessage()
    {
        var ipcClient = new SequencedLauncherIpcClient(false, true);
        var guiController = new FixedLauncherGuiController(LauncherGuiInstanceState.RunningInCurrentSession, startResult: false);
        var helper = new LauncherIpcHelper(ipcClient, guiController, new NoOpLauncherWaitDelay(), new RecordingLauncherUserNotifier());

        var response = helper.SendWithAutoStart(new IpcMessage { Command = "probe" });

        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal(2, ipcClient.PingCount);
        Assert.Equal(1, ipcClient.SendCount);
        Assert.Equal(0, guiController.StartCount);
    }

    [Fact]
    public void ExistingGuiDisappearsDuringReadinessWaitStartsNewGui()
    {
        var ipcClient = new SequencedLauncherIpcClient(false, true);
        var guiController = new SequencedLauncherGuiController(
            startResult: true,
            LauncherGuiInstanceState.RunningInCurrentSession,
            LauncherGuiInstanceState.NotRunning,
            LauncherGuiInstanceState.NotRunning,
            LauncherGuiInstanceState.RunningInCurrentSession,
            LauncherGuiInstanceState.RunningInCurrentSession);
        var helper = new LauncherIpcHelper(ipcClient, guiController, new NoOpLauncherWaitDelay(), new RecordingLauncherUserNotifier());

        var response = helper.SendWithAutoStart(new IpcMessage { Command = "probe" });

        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal(2, ipcClient.PingCount);
        Assert.Equal(1, ipcClient.SendCount);
        Assert.Equal(1, guiController.StartCount);
    }

    [Fact]
    public void ClosedGuiRunAsStartupRequestsStartupUnlockGrant()
    {
        var ipcClient = new SequencedLauncherIpcClient(true);
        var guiController = new SequencedLauncherGuiController(
            startResult: true,
            LauncherGuiInstanceState.NotRunning,
            LauncherGuiInstanceState.RunningInCurrentSession,
            LauncherGuiInstanceState.RunningInCurrentSession);
        var helper = new LauncherIpcHelper(ipcClient, guiController, new NoOpLauncherWaitDelay(), new RecordingLauncherUserNotifier());

        var response = helper.SendWithAutoStart(new IpcMessage
        {
            Command = IpcCommands.Launch,
            AppId = @"C:\tools\app.exe"
        });

        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.True(guiController.LastGrantStartupRunAsUnlock);
    }

    [Fact]
    public void ClosedGuiNonRunAsStartupDoesNotRequestStartupUnlockGrant()
    {
        var ipcClient = new SequencedLauncherIpcClient(true);
        var guiController = new SequencedLauncherGuiController(
            startResult: true,
            LauncherGuiInstanceState.NotRunning,
            LauncherGuiInstanceState.RunningInCurrentSession,
            LauncherGuiInstanceState.RunningInCurrentSession);
        var helper = new LauncherIpcHelper(ipcClient, guiController, new NoOpLauncherWaitDelay(), new RecordingLauncherUserNotifier());

        var response = helper.SendWithAutoStart(new IpcMessage
        {
            Command = IpcCommands.OpenFolder,
            Arguments = @"C:\tools"
        });

        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.False(guiController.LastGrantStartupRunAsUnlock);
    }

    [Fact]
    public void DifferentSessionGuiStartsNewGuiInsteadOfWaitingForIpc()
    {
        var ipcClient = new SequencedLauncherIpcClient(true);
        var guiController = new SequencedLauncherGuiController(
            startResult: true,
            LauncherGuiInstanceState.RunningInDifferentSession,
            LauncherGuiInstanceState.RunningInCurrentSession,
            LauncherGuiInstanceState.RunningInCurrentSession);
        var helper = new LauncherIpcHelper(ipcClient, guiController, new NoOpLauncherWaitDelay(), new RecordingLauncherUserNotifier());

        var response = helper.SendWithAutoStart(new IpcMessage { Command = "probe" });

        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal(1, ipcClient.PingCount);
        Assert.Equal(1, ipcClient.SendCount);
        Assert.Equal(1, guiController.StartCount);
    }

    [Fact]
    public void DifferentSessionGuiWaitsForCurrentSessionOwnershipBeforePingingIpc()
    {
        var ipcClient = new SequencedLauncherIpcClient(true);
        var guiController = new SequencedLauncherGuiController(
            startResult: true,
            LauncherGuiInstanceState.RunningInDifferentSession,
            LauncherGuiInstanceState.RunningInDifferentSession,
            LauncherGuiInstanceState.RunningInCurrentSession,
            LauncherGuiInstanceState.RunningInCurrentSession);
        var waitDelay = new RecordingLauncherWaitDelay();
        var helper = new LauncherIpcHelper(ipcClient, guiController, waitDelay, new RecordingLauncherUserNotifier());

        var response = helper.SendWithAutoStart(new IpcMessage { Command = "probe" });

        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal(1, ipcClient.PingCount);
        Assert.Equal([500], waitDelay.Delays);
    }

    private sealed class SequencedLauncherIpcClient(params bool[] pingResults) : ILauncherIpcClient
    {
        private int _pingIndex;

        public int PingCount { get; private set; }
        public int SendCount { get; private set; }

        public bool PingServer()
        {
            PingCount++;
            if (_pingIndex >= pingResults.Length)
                return pingResults[^1];
            return pingResults[_pingIndex++];
        }

        public IpcResponse? SendMessage(IpcMessage message)
        {
            SendCount++;
            return new IpcResponse { Success = true };
        }
    }

    private sealed class FixedLauncherGuiController(LauncherGuiInstanceState guiState, bool startResult) : ILauncherGuiController
    {
        public int StartCount { get; private set; }
        public bool LastGrantStartupRunAsUnlock { get; private set; }

        public LauncherGuiInstanceState GetGuiState() => guiState;

        public bool StartGui(bool grantStartupRunAsUnlock)
        {
            StartCount++;
            LastGrantStartupRunAsUnlock = grantStartupRunAsUnlock;
            return startResult;
        }
    }

    private sealed class SequencedLauncherGuiController(
        bool startResult,
        params LauncherGuiInstanceState[] guiStates) : ILauncherGuiController
    {
        private int _stateIndex;

        public int StartCount { get; private set; }
        public bool LastGrantStartupRunAsUnlock { get; private set; }

        public LauncherGuiInstanceState GetGuiState()
        {
            if (_stateIndex >= guiStates.Length)
                return guiStates[^1];
            return guiStates[_stateIndex++];
        }

        public bool StartGui(bool grantStartupRunAsUnlock)
        {
            StartCount++;
            LastGrantStartupRunAsUnlock = grantStartupRunAsUnlock;
            return startResult;
        }
    }

    private sealed class NoOpLauncherWaitDelay : ILauncherWaitDelay
    {
        public void Sleep(int milliseconds)
        {
        }
    }

    private sealed class RecordingLauncherWaitDelay : ILauncherWaitDelay
    {
        public List<int> Delays { get; } = [];

        public void Sleep(int milliseconds)
        {
            Delays.Add(milliseconds);
        }
    }

    private sealed class RecordingLauncherUserNotifier : ILauncherUserNotifier
    {
        public List<string> Errors { get; } = [];
        public List<string> Warnings { get; } = [];

        public void ShowError(string message)
        {
            Errors.Add(message);
        }

        public void ShowWarning(string message)
        {
            Warnings.Add(message);
        }
    }
}
