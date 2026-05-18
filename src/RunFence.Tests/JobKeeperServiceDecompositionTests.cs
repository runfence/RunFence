using System.IO.Pipes;
using System.Security.Principal;
using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public sealed class JobKeeperServiceDecompositionTests
{
    private const string Sid = "S-1-5-21-100-200-300-1001";

    [Fact]
    public void WaitAndRegisterJobKeeper_ConnectionFails_TerminatesExpectedProcess()
    {
        var identity = Identity(isLow: false);
        var terminator = new Mock<IJobKeeperProcessTerminator>();
        var service = new JobKeeperService(
            new Mock<ILoggingService>().Object,
            new Mock<IJobKeeperIdentityStore>().Object,
            new Mock<IJobKeeperPipeServerFactory>().Object,
            new Mock<IJobKeeperProcessDiscovery>().Object,
            new Mock<IJobKeeperProcessVerifier>().Object,
            new JobKeeperRegistry(),
            terminator.Object,
            TimeSpan.FromSeconds(10));
        var pipe = new NamedPipeServerStream($"test-{Guid.NewGuid():N}", PipeDirection.InOut);
        pipe.Dispose();

        var result = service.WaitAndRegisterJobKeeper(identity, pipe, 1234, new SecurityIdentifier(Sid));

        Assert.Equal(0, result);
        terminator.Verify(t => t.Kill(1234), Times.Once);
    }

    [Fact]
    public void WaitAndRegisterJobKeeper_TimeoutUsesInjectedValue()
    {
        var identity = Identity(isLow: false);
        var terminator = new Mock<IJobKeeperProcessTerminator>();
        var service = new JobKeeperService(
            new Mock<ILoggingService>().Object,
            new Mock<IJobKeeperIdentityStore>().Object,
            new Mock<IJobKeeperPipeServerFactory>().Object,
            new Mock<IJobKeeperProcessDiscovery>().Object,
            new Mock<IJobKeeperProcessVerifier>().Object,
            new JobKeeperRegistry(),
            terminator.Object,
            TimeSpan.FromMilliseconds(20));
        using var pipe = new NamedPipeServerStream($"test-{Guid.NewGuid():N}", PipeDirection.InOut);

        var result = service.WaitAndRegisterJobKeeper(identity, pipe, 4321, new SecurityIdentifier(Sid));

        Assert.Equal(0, result);
        terminator.Verify(t => t.Kill(4321), Times.Once);
    }

    [Fact]
    public void TryReconnectExistingJobKeeper_PipeCreationFails_RemovesStaleIdentity()
    {
        var identity = Identity(isLow: false);
        var identityStore = new Mock<IJobKeeperIdentityStore>();
        var pipeFactory = new Mock<IJobKeeperPipeServerFactory>();
        var discovery = new Mock<IJobKeeperProcessDiscovery>();
        var verifier = new Mock<IJobKeeperProcessVerifier>();
        var terminator = new Mock<IJobKeeperProcessTerminator>();
        var registry = new JobKeeperRegistry();
        identityStore.Setup(s => s.Get(Sid, false)).Returns(identity);
        discovery.Setup(d => d.FindRunningJobKeeperPid(It.IsAny<SecurityIdentifier>(), false)).Returns(1234);
        pipeFactory.Setup(f => f.Create(identity, It.IsAny<SecurityIdentifier>()))
            .Throws(new IOException("stale pipe"));

        var service = new JobKeeperService(
            new Mock<ILoggingService>().Object,
            identityStore.Object,
            pipeFactory.Object,
            discovery.Object,
            verifier.Object,
            registry,
            terminator.Object,
            TimeSpan.FromSeconds(10));

        var result = service.TryReconnectExistingJobKeeper(Sid, false, new SecurityIdentifier(Sid));

        Assert.Equal(0, result);
        identityStore.Verify(s => s.Remove(Sid, false), Times.Once);
        terminator.Verify(t => t.Kill(1234), Times.Once);
        Assert.False(registry.Has(Sid, false));
    }

    [Fact]
    public async Task WaitAndRegisterJobKeeper_VerifierFails_LogsExactReasonAndTerminatesExpectedProcess()
    {
        var identity = Identity(isLow: false);
        var log = new Mock<ILoggingService>();
        var identityStore = new Mock<IJobKeeperIdentityStore>();
        var verifier = new Mock<IJobKeeperProcessVerifier>();
        var terminator = new Mock<IJobKeeperProcessTerminator>();
        var registry = new JobKeeperRegistry();
        var pipeName = $"test-{Guid.NewGuid():N}";
        using var pipeServer = new NamedPipeServerStream(pipeName, PipeDirection.InOut);
        using var pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        verifier.Setup(v => v.Verify(pipeServer, 1234, It.IsAny<SecurityIdentifier>(), identity))
            .Returns(JobKeeperProcessVerificationResult.Failure("pipe client image mismatch: expected keeper."));
        var connectTask = Task.Run(() => pipeClient.Connect(1000));
        var service = new JobKeeperService(
            log.Object,
            identityStore.Object,
            new Mock<IJobKeeperPipeServerFactory>().Object,
            new Mock<IJobKeeperProcessDiscovery>().Object,
            verifier.Object,
            registry,
            terminator.Object,
            TimeSpan.FromSeconds(10));

        var result = service.WaitAndRegisterJobKeeper(identity, pipeServer, 1234, new SecurityIdentifier(Sid));

        await connectTask;
        Assert.Equal(0, result);
        log.Verify(l => l.Warn(It.Is<string>(message =>
            message.Contains("verification failed", StringComparison.Ordinal)
            && message.Contains("pipe client image mismatch", StringComparison.Ordinal))), Times.Once);
        terminator.Verify(t => t.Kill(1234), Times.Once);
        identityStore.Verify(s => s.UpdateLastVerifiedPid(It.IsAny<JobKeeperInstanceIdentity>(), It.IsAny<int>()), Times.Never);
        Assert.False(registry.Has(Sid, false));
    }

    [Fact]
    public void TryReconnectExistingJobKeeper_DiscoveredPidDiffersFromPersistedPid_KillsDiscoveredPidOnly()
    {
        var identity = Identity(isLow: false) with { LastVerifiedKeeperPid = 1234 };
        var identityStore = new Mock<IJobKeeperIdentityStore>();
        var pipeFactory = new Mock<IJobKeeperPipeServerFactory>();
        var discovery = new Mock<IJobKeeperProcessDiscovery>();
        var terminator = new Mock<IJobKeeperProcessTerminator>();
        identityStore.Setup(s => s.Get(Sid, false)).Returns(identity);
        discovery.Setup(d => d.FindRunningJobKeeperPid(It.IsAny<SecurityIdentifier>(), false)).Returns(5678);

        var service = new JobKeeperService(
            new Mock<ILoggingService>().Object,
            identityStore.Object,
            pipeFactory.Object,
            discovery.Object,
            new Mock<IJobKeeperProcessVerifier>().Object,
            new JobKeeperRegistry(),
            terminator.Object,
            TimeSpan.FromSeconds(10));

        var result = service.TryReconnectExistingJobKeeper(Sid, false, new SecurityIdentifier(Sid));

        Assert.Equal(0, result);
        terminator.Verify(t => t.Kill(5678), Times.Once);
        terminator.Verify(t => t.Kill(1234), Times.Never);
        identityStore.Verify(s => s.Remove(Sid, false), Times.Once);
        pipeFactory.Verify(f => f.Create(It.IsAny<JobKeeperInstanceIdentity>(), It.IsAny<SecurityIdentifier>()), Times.Never);
    }

    [Fact]
    public async Task JobKeeperLaunchIpcClient_PipeFailure_RemovesRegisteredKeeper()
    {
        var registry = new JobKeeperRegistry();
        var terminator = new Mock<IJobKeeperProcessTerminator>();
        var pipe = new NamedPipeServerStream($"test-{Guid.NewGuid():N}", PipeDirection.InOut);
        registry.Register(Sid, false, new JobKeeperState(pipe, 1234));
        pipe.Dispose();

        var client = new JobKeeperLaunchIpcClient(new Mock<ILoggingService>().Object, registry, terminator.Object);

        var result = await client.SendLaunchRequestAsync(
            Sid,
            false,
            new JobKeeperLaunchRequest("app.exe", null, null, false, false, null),
            TimeSpan.FromMilliseconds(100),
            CancellationToken.None);

        Assert.Equal(0, result);
        Assert.False(registry.Has(Sid, false));
        terminator.Verify(t => t.Kill(1234), Times.Once);
    }

    [Fact]
    public async Task JobKeeperLaunchIpcClient_Timeout_RemovesRegisteredKeeper()
    {
        var registry = new JobKeeperRegistry();
        var terminator = new Mock<IJobKeeperProcessTerminator>();
        using var pair = ConnectedPipePair.Create();
        registry.Register(Sid, false, new JobKeeperState(pair.Server, 1234));

        var client = new JobKeeperLaunchIpcClient(new Mock<ILoggingService>().Object, registry, terminator.Object);

        var result = await client.SendLaunchRequestAsync(
            Sid,
            false,
            new JobKeeperLaunchRequest("app.exe", null, null, false, false, null),
            TimeSpan.FromMilliseconds(100),
            CancellationToken.None);

        Assert.Equal(0, result);
        Assert.False(registry.Has(Sid, false));
        terminator.Verify(t => t.Kill(1234), Times.Once);
    }

    [Fact]
    public async Task JobKeeperLaunchIpcClient_InvalidResponse_RemovesRegisteredKeeper()
    {
        var registry = new JobKeeperRegistry();
        var terminator = new Mock<IJobKeeperProcessTerminator>();
        using var pair = ConnectedPipePair.Create();
        registry.Register(Sid, false, new JobKeeperState(pair.Server, 1234));

        var responder = Task.Run(async () =>
        {
            _ = JobKeeperProtocol.ReadMessage<JobKeeperLaunchRequest>(pair.Client);
            var invalidJson = System.Text.Encoding.UTF8.GetBytes("{bad");
            await pair.Client.WriteAsync(BitConverter.GetBytes(invalidJson.Length));
            await pair.Client.WriteAsync(invalidJson);
            await pair.Client.FlushAsync();
        });

        var client = new JobKeeperLaunchIpcClient(new Mock<ILoggingService>().Object, registry, terminator.Object);
        var result = await client.SendLaunchRequestAsync(
            Sid,
            false,
            new JobKeeperLaunchRequest("app.exe", null, null, false, false, null),
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        await responder;
        Assert.Equal(0, result);
        Assert.False(registry.Has(Sid, false));
        terminator.Verify(t => t.Kill(1234), Times.Once);
    }

    [Fact]
    public async Task JobKeeperLaunchIpcClient_KeeperLaunchError_ThrowsDetailedWin32Message()
    {
        var registry = new JobKeeperRegistry();
        var terminator = new Mock<IJobKeeperProcessTerminator>();
        using var pair = ConnectedPipePair.Create();
        registry.Register(Sid, false, new JobKeeperState(pair.Server, 1234));

        var responder = Task.Run(() =>
        {
            _ = JobKeeperProtocol.ReadMessage<JobKeeperLaunchRequest>(pair.Client);
            JobKeeperProtocol.WriteMessage(pair.Client, new JobKeeperLaunchResponse(0, 2));
        });

        var client = new JobKeeperLaunchIpcClient(new Mock<ILoggingService>().Object, registry, terminator.Object);

        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => client.SendLaunchRequestAsync(
            Sid,
            false,
            new JobKeeperLaunchRequest(@"C:\Missing\app.exe", null, null, false, false, null),
            TimeSpan.FromSeconds(1),
            CancellationToken.None));

        await responder;
        Assert.Contains(@"C:\Missing\app.exe", ex.Message);
        Assert.Contains("Win32 error (0x00000002)", ex.Message);
        Assert.Contains("cannot find the file", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(registry.Has(Sid, false));
        terminator.Verify(t => t.Kill(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task JobKeeperLaunchIpcClient_DoesNotHoldSyncRootDuringPipeIo()
    {
        var registry = new JobKeeperRegistry();
        var terminator = new Mock<IJobKeeperProcessTerminator>();
        using var pair = ConnectedPipePair.Create();
        var state = new JobKeeperState(pair.Server, 1234);
        registry.Register(Sid, false, state);
        var requestRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var responder = Task.Run(async () =>
        {
            _ = JobKeeperProtocol.ReadMessage<JobKeeperLaunchRequest>(pair.Client);
            requestRead.TrySetResult();
            await releaseResponse.Task;
            JobKeeperProtocol.WriteMessage(pair.Client, new JobKeeperLaunchResponse(4321, 0));
        });

        var client = new JobKeeperLaunchIpcClient(new Mock<ILoggingService>().Object, registry, terminator.Object);
        var launchTask = client.SendLaunchRequestAsync(
            Sid,
            false,
            new JobKeeperLaunchRequest("app.exe", null, null, false, false, null),
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        await requestRead.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var lockTaken = false;
        try
        {
            Monitor.TryEnter(state.SyncRoot, TimeSpan.FromMilliseconds(100), ref lockTaken);
            Assert.True(lockTaken);
        }
        finally
        {
            if (lockTaken)
                Monitor.Exit(state.SyncRoot);
        }

        releaseResponse.TrySetResult();
        var result = await launchTask;
        await responder;

        Assert.Equal(4321, result);
        terminator.Verify(t => t.Kill(It.IsAny<int>()), Times.Never);
    }

    private static JobKeeperInstanceIdentity Identity(bool isLow) => new()
    {
        TargetSid = Sid,
        ExpectedMode = JobKeeperInstanceIdentity.GetMode(isLow),
        InstanceId = "instance",
        PipeName = $"pipe-{Guid.NewGuid():N}",
        JobName = $"job-{Guid.NewGuid():N}",
    };

    private sealed class ConnectedPipePair : IDisposable
    {
        private ConnectedPipePair(NamedPipeServerStream server, NamedPipeClientStream client)
        {
            Server = server;
            Client = client;
        }

        public NamedPipeServerStream Server { get; }
        public NamedPipeClientStream Client { get; }

        public static ConnectedPipePair Create()
        {
            var pipeName = $"RunFenceTest_JobKeeper_{Guid.NewGuid():N}";
            var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            var connectTask = server.WaitForConnectionAsync();
            client.Connect(1000);
            connectTask.GetAwaiter().GetResult();
            return new ConnectedPipePair(server, client);
        }

        public void Dispose()
        {
            Client.Dispose();
            Server.Dispose();
        }
    }
}
