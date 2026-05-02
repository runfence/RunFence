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
            terminator.Object);
        var pipe = new NamedPipeServerStream($"test-{Guid.NewGuid():N}", PipeDirection.InOut);
        pipe.Dispose();

        var result = service.WaitAndRegisterJobKeeper(identity, pipe, 1234, new SecurityIdentifier(Sid));

        Assert.Equal(0, result);
        terminator.Verify(t => t.Kill(1234), Times.Once);
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
            terminator.Object);

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
        var connectTask = Task.Run(() => pipeClient.Connect());
        var service = new JobKeeperService(
            log.Object,
            identityStore.Object,
            new Mock<IJobKeeperPipeServerFactory>().Object,
            new Mock<IJobKeeperProcessDiscovery>().Object,
            verifier.Object,
            registry,
            terminator.Object);

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
            terminator.Object);

        var result = service.TryReconnectExistingJobKeeper(Sid, false, new SecurityIdentifier(Sid));

        Assert.Equal(0, result);
        terminator.Verify(t => t.Kill(5678), Times.Once);
        terminator.Verify(t => t.Kill(1234), Times.Never);
        identityStore.Verify(s => s.Remove(Sid, false), Times.Once);
        pipeFactory.Verify(f => f.Create(It.IsAny<JobKeeperInstanceIdentity>(), It.IsAny<SecurityIdentifier>()), Times.Never);
    }

    [Fact]
    public void JobKeeperLaunchIpcClient_PipeFailure_RemovesRegisteredKeeper()
    {
        var registry = new JobKeeperRegistry();
        var terminator = new Mock<IJobKeeperProcessTerminator>();
        var pipe = new NamedPipeServerStream($"test-{Guid.NewGuid():N}", PipeDirection.InOut);
        registry.Register(Sid, false, new JobKeeperState(pipe, 1234));
        pipe.Dispose();

        var client = new JobKeeperLaunchIpcClient(new Mock<ILoggingService>().Object, registry, terminator.Object);

        var result = client.SendLaunchRequest(Sid, false, new JobKeeperLaunchRequest("app.exe", null, null, false, null));

        Assert.Equal(0, result);
        Assert.False(registry.Has(Sid, false));
        terminator.Verify(t => t.Kill(1234), Times.Once);
    }

    private static JobKeeperInstanceIdentity Identity(bool isLow) => new()
    {
        TargetSid = Sid,
        ExpectedMode = JobKeeperInstanceIdentity.GetMode(isLow),
        InstanceId = "instance",
        PipeName = $"pipe-{Guid.NewGuid():N}",
        JobName = $"job-{Guid.NewGuid():N}",
    };
}
