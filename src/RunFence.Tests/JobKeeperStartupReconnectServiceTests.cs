using System.Security.Principal;
using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public sealed class JobKeeperStartupReconnectServiceTests
{
    private const string Sid = "S-1-5-21-100-200-300-1001";

    private readonly Mock<IJobKeeperIdentityStore> _identityStore = new();
    private readonly Mock<IJobKeeperService> _jobKeeperService = new();
    private readonly Mock<IVerifiedRestrictedJobCache> _cache = new();
    private readonly Mock<ILoggingService> _log = new();

    [Fact]
    public async Task RunInitialReconnectAsync_ReconnectsPersistedKeeperIdentitiesAndRaisesCompletionOnce()
    {
        _identityStore.Setup(s => s.GetAll()).Returns(
        [
            Identity(Sid, JobKeeperIntegrityMode.Restricted),
            Identity("S-1-5-21-100-200-300-1002", JobKeeperIntegrityMode.LowIntegrity),
        ]);
        _jobKeeperService
            .Setup(s => s.TryReconnectExistingJobKeeper(
                Sid,
                false,
                It.Is<SecurityIdentifier>(sid => sid.Value == Sid)))
            .Returns(1234);
        _jobKeeperService
            .Setup(s => s.TryReconnectExistingJobKeeper(
                "S-1-5-21-100-200-300-1002",
                true,
                It.Is<SecurityIdentifier>(sid => sid.Value == "S-1-5-21-100-200-300-1002")))
            .Returns(0);

        using var service = CreateService();
        JobKeeperStartupReconnectCompletedEventArgs? completion = null;
        var count = 0;
        service.StartupReconnectCompleted += (_, e) =>
        {
            completion = e;
            count++;
        };

        await service.RunInitialReconnectAsync(CancellationToken.None);
        await service.RunInitialReconnectAsync(CancellationToken.None);

        Assert.NotNull(completion);
        Assert.Equal(1, completion!.ReconnectedCount);
        Assert.True(completion.Succeeded);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task RunInitialReconnectAsync_PerIdentityFailure_LogsAndContinues()
    {
        var secondSid = "S-1-5-21-100-200-300-1002";
        _identityStore.Setup(s => s.GetAll()).Returns(
        [
            Identity("not-a-sid", JobKeeperIntegrityMode.Restricted),
            Identity(secondSid, JobKeeperIntegrityMode.Restricted),
        ]);
        _jobKeeperService
            .Setup(s => s.TryReconnectExistingJobKeeper(secondSid, false, It.IsAny<SecurityIdentifier>()))
            .Returns(5678);

        using var service = CreateService();
        JobKeeperStartupReconnectCompletedEventArgs? completion = null;
        service.StartupReconnectCompleted += (_, e) => completion = e;

        await service.RunInitialReconnectAsync(CancellationToken.None);

        Assert.NotNull(completion);
        Assert.Equal(1, completion!.ReconnectedCount);
        _log.Verify(l => l.Warn(It.Is<string>(message => message.Contains("failed to reconnect keeper"))), Times.Once);
    }

    [Fact]
    public async Task RunInitialReconnectAsync_Cancellation_RaisesFailedCompletionAndThrows()
    {
        _identityStore.Setup(s => s.GetAll()).Returns(
        [
            Identity(Sid, JobKeeperIntegrityMode.Restricted),
        ]);

        using var service = CreateService();
        JobKeeperStartupReconnectCompletedEventArgs? completion = null;
        service.StartupReconnectCompleted += (_, e) => completion = e;
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.RunInitialReconnectAsync(cts.Token));

        Assert.NotNull(completion);
        Assert.False(completion!.Succeeded);
        Assert.Equal("Canceled", completion.FailureMessage);
    }

    [Fact]
    public void Start_RunsInitialReconnectInBackgroundAndRaisesCompletion()
    {
        _identityStore.Setup(s => s.GetAll()).Returns(
        [
            Identity(Sid, JobKeeperIntegrityMode.Restricted),
        ]);
        _jobKeeperService
            .Setup(s => s.TryReconnectExistingJobKeeper(
                Sid,
                false,
                It.Is<SecurityIdentifier>(sid => sid.Value == Sid)))
            .Returns(1234);

        using var service = CreateService();
        using var completionEvent = new ManualResetEventSlim();
        JobKeeperStartupReconnectCompletedEventArgs? completion = null;
        service.StartupReconnectCompleted += (_, e) =>
        {
            completion = e;
            completionEvent.Set();
        };

        service.Start();

        Assert.True(completionEvent.Wait(TimeSpan.FromSeconds(2)));
        Assert.NotNull(completion);
        Assert.Equal(1, completion!.ReconnectedCount);
        Assert.True(completion.Succeeded);
        Assert.Null(completion.FailureMessage);
        _identityStore.Verify(s => s.GetAll(), Times.Once);
        _jobKeeperService.Verify(
            s => s.TryReconnectExistingJobKeeper(
                Sid,
                false,
                It.Is<SecurityIdentifier>(sid => sid.Value == Sid)),
            Times.Once);
    }

    [Fact]
    public void Start_CalledTwice_StartsOnlyOneBackgroundReconnect()
    {
        using var getAllStarted = new ManualResetEventSlim();
        using var releaseGetAll = new ManualResetEventSlim();
        using var completionEvent = new ManualResetEventSlim();
        JobKeeperStartupReconnectCompletedEventArgs? completion = null;
        var getAllCallCount = 0;
        _identityStore
            .Setup(s => s.GetAll())
            .Returns(() =>
            {
                Interlocked.Increment(ref getAllCallCount);
                getAllStarted.Set();
                Assert.True(releaseGetAll.Wait(TimeSpan.FromSeconds(2)));
                return [];
            });

        using var service = CreateService();
        service.StartupReconnectCompleted += (_, e) =>
        {
            completion = e;
            completionEvent.Set();
        };

        service.Start();
        Assert.True(getAllStarted.Wait(TimeSpan.FromSeconds(2)));

        service.Start();
        Assert.Equal(1, Volatile.Read(ref getAllCallCount));

        releaseGetAll.Set();

        Assert.True(completionEvent.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal(0, completion?.ReconnectedCount ?? 0);
        Assert.Null(completion?.FailureMessage);
        Assert.Equal(1, Volatile.Read(ref getAllCallCount));
        _identityStore.Verify(s => s.GetAll(), Times.Once);
    }

    [Fact]
    public void RunMaintenanceCycle_SweepsVerifiedRestrictedJobCache()
    {
        using var service = CreateService();

        service.RunMaintenanceCycle();

        _cache.Verify(c => c.SweepEmptyOrInvalidJobs(), Times.Once);
    }

    private JobKeeperStartupReconnectService CreateService() =>
        new(
            _identityStore.Object,
            _jobKeeperService.Object,
            _cache.Object,
            _log.Object);

    private static JobKeeperInstanceIdentity Identity(string sid, JobKeeperIntegrityMode mode) => new()
    {
        TargetSid = sid,
        ExpectedMode = mode,
        InstanceId = Guid.NewGuid().ToString("N"),
        PipeName = $"pipe-{Guid.NewGuid():N}",
    };
}
