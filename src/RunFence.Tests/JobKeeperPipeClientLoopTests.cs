using System.IO.Pipes;
using RunFence.Core;
using RunFence.JobKeeper;
using Xunit;

namespace RunFence.Tests;

public sealed class JobKeeperPipeClientLoopTests
{
    [Fact]
    public async Task RunOnce_NoRequestsAndLifetimeExpiresWhileWaiting_ReturnsFalse()
    {
        var pipeName = $"RunFenceTest_JobKeeperPipe_{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var acceptTask = server.WaitForConnectionAsync();
        var requestHandler = new RecordingRequestHandler();
        var lifetimeController = new SequenceLifetimeController(false, true);
        var loop = new JobKeeperPipeClientLoop(
            new JobKeeperStartupOptions(pipeName),
            requestHandler,
            lifetimeController);

        var shouldContinue = loop.RunOnce();

        await acceptTask;

        Assert.False(shouldContinue);
        Assert.Empty(requestHandler.HandledRequests);
        Assert.Equal(2, lifetimeController.ShouldExitCallCount);
    }

    private sealed class RecordingRequestHandler : IJobKeeperRequestHandler
    {
        public List<JobKeeperLaunchRequest> HandledRequests { get; } = [];

        public JobKeeperLaunchResponse Handle(JobKeeperLaunchRequest request)
        {
            HandledRequests.Add(request);
            return new JobKeeperLaunchResponse(1234, 0);
        }
    }

    private sealed class SequenceLifetimeController(params bool[] shouldExitResults) : IJobKeeperLifetimeController
    {
        private readonly Queue<bool> _shouldExitResults = new(shouldExitResults);

        public int ShouldExitCallCount { get; private set; }

        public void RecordRequestArrival()
        {
        }

        public bool ShouldExit()
        {
            ShouldExitCallCount++;
            if (_shouldExitResults.Count == 0)
                return false;

            return _shouldExitResults.Dequeue();
        }
    }
}
