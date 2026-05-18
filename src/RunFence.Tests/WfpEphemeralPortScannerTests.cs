using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Firewall.Wfp;
using RunFence.Infrastructure;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class WfpEphemeralPortScannerTests
{
    private const string OwnSid = "S-1-5-21-1234-own";
    private const string OtherSid = "S-1-5-21-1234-other";
    private const string AnotherSid = "S-1-5-21-1234-another";

    private static PortRange P(int port) => new(port, port);
    private static PortRange R(int low, int high) => new(low, high);

    private static PortOwnerSet Owners(params string?[] owners)
    {
        var set = new PortOwnerSet();
        foreach (var owner in owners)
            set.AddOwner(owner);
        return set;
    }

    [Fact]
    public void ComputeBlockedEphemeralRanges_OwnedBySameSid_Excluded()
    {
        var result = WfpEphemeralPortScanner.ComputeBlockedEphemeralRanges(
            OwnSid,
            [R(49152, 65535)],
            new Dictionary<int, PortOwnerSet> { [55000] = Owners(OwnSid) });

        Assert.Empty(result);
    }

    [Fact]
    public void ComputeBlockedEphemeralRanges_CrossUserPorts_Coalesced()
    {
        var result = WfpEphemeralPortScanner.ComputeBlockedEphemeralRanges(
            OwnSid,
            [R(49152, 65535)],
            new Dictionary<int, PortOwnerSet>
            {
                [55000] = Owners(OtherSid),
                [55001] = Owners(OtherSid),
                [55003] = Owners((string?)null)
            });

        Assert.Equal([R(55000, 55001), P(55003)], result);
    }

    [Fact]
    public void ComputeBlockedEphemeralRanges_SinglePortExemption_WinsOverRange()
    {
        var result = WfpEphemeralPortScanner.ComputeBlockedEphemeralRanges(
            OwnSid,
            [R(49152, 65535), P(55000)],
            new Dictionary<int, PortOwnerSet> { [55000] = Owners(OtherSid) });

        Assert.Empty(result);
    }

    [Fact]
    public void ComputeBlockedEphemeralRanges_UnknownOwnerAlongsideOwnSid_BlocksThePort()
    {
        var result = WfpEphemeralPortScanner.ComputeBlockedEphemeralRanges(
            OwnSid,
            [R(49152, 65535)],
            new Dictionary<int, PortOwnerSet> { [55000] = Owners(OwnSid, null) });

        Assert.Equal([P(55000)], result);
    }

    [Fact]
    public void ComputeBlockedEphemeralRanges_MultipleDistinctOwnerSids_BlockThePort()
    {
        var result = WfpEphemeralPortScanner.ComputeBlockedEphemeralRanges(
            OwnSid,
            [R(49152, 65535)],
            new Dictionary<int, PortOwnerSet> { [55000] = Owners(OtherSid, AnotherSid) });

        Assert.Equal([P(55000)], result);
    }

    [Fact]
    public void ScanAndApply_EligibleAndClearAccounts_RouteToCorrectTargets()
    {
        var snapshotProvider = new RecordingSnapshotProvider(
            new Dictionary<int, PortOwnerSet> { [55000] = Owners(OtherSid) });
        var (blocker, scanner) = CreateScanner(
            MakeDatabase(
                new AccountEntry
                {
                    Sid = OwnSid,
                    Firewall = new FirewallAccountSettings
                    {
                        AllowLocalhost = false,
                        FilterEphemeralLoopback = true,
                        LocalhostPortExemptions = ["49152-65535"]
                    }
                },
                new AccountEntry
                {
                    Sid = OtherSid,
                    Firewall = new FirewallAccountSettings
                    {
                        AllowLocalhost = false,
                        FilterEphemeralLoopback = false
                    }
                }),
            snapshotProvider);

        scanner.Start();

        Assert.Equal(1, snapshotProvider.CallCount);
        blocker.Verify(
            b => b.UpdateEphemeralPorts(
                OwnSid,
                It.Is<IReadOnlyList<PortRange>>(ranges =>
                    ranges.Count == 1 &&
                    ranges[0].Low == 55000 &&
                    ranges[0].High == 55000)),
            Times.Once);
        blocker.Verify(b => b.UpdateEphemeralPorts(OtherSid, It.Is<IReadOnlyList<PortRange>>(ranges => ranges.Count == 0)), Times.Once);
    }

    [Fact]
    public void ScanAndApply_NoEligibleAccounts_DoesNotCollectSnapshot()
    {
        var snapshotProvider = new RecordingSnapshotProvider(
            new Dictionary<int, PortOwnerSet> { [55000] = Owners(OtherSid) });
        var (blocker, scanner) = CreateScanner(
            MakeDatabase(
                new AccountEntry
                {
                    Sid = OwnSid,
                    Firewall = new FirewallAccountSettings
                    {
                        AllowLocalhost = false,
                        FilterEphemeralLoopback = false
                    }
                },
                new AccountEntry
                {
                    Sid = AnotherSid,
                    Firewall = new FirewallAccountSettings
                    {
                        AllowLocalhost = true,
                        FilterEphemeralLoopback = true
                    }
                }),
            snapshotProvider);

        scanner.Start();

        Assert.Equal(0, snapshotProvider.CallCount);
        blocker.Verify(b => b.UpdateEphemeralPorts(OwnSid, It.Is<IReadOnlyList<PortRange>>(ranges => ranges.Count == 0)), Times.Once);
        blocker.Verify(b => b.UpdateEphemeralPorts(AnotherSid, It.IsAny<IReadOnlyList<PortRange>>()), Times.Never);
    }

    [Fact]
    public void ScanAndApply_CreatesDatabaseSnapshotOnUiThread()
    {
        var snapshotProvider = new RecordingSnapshotProvider(new Dictionary<int, PortOwnerSet>());
        var blocker = new Mock<IWfpLocalhostBlocker>();
        var log = new Mock<ILoggingService>();
        var database = MakeDatabase(new AccountEntry
        {
            Sid = OwnSid,
            Firewall = new FirewallAccountSettings { AllowLocalhost = true }
        });

        var insideUiInvoke = false;
        var providerCalledInsideUiInvoke = false;
        var uiThreadInvoker = new Mock<IUiThreadInvoker>();
        uiThreadInvoker
            .Setup(u => u.Invoke(It.IsAny<Func<AppDatabase>>()))
            .Returns<Func<AppDatabase>>(f =>
            {
                insideUiInvoke = true;
                try
                {
                    return f();
                }
                finally
                {
                    insideUiInvoke = false;
                }
            });
        var dbAccessor = new UiThreadDatabaseAccessor(
            new LambdaDatabaseProvider(() =>
            {
                providerCalledInsideUiInvoke = insideUiInvoke;
                return database;
            }),
            () => uiThreadInvoker.Object);

        var scanner = new WfpEphemeralPortScanner(blocker.Object, dbAccessor, snapshotProvider, log.Object, startTimer: false);

        scanner.Start();

        Assert.True(providerCalledInsideUiInvoke);
    }

    [Fact]
    public async Task TimerTick_ReentrantCall_DoesNotRunSecondScan()
    {
        var firstCallStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseScan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = new Mock<IWfpLocalhostBlocker>();
        blocker
            .Setup(b => b.UpdateEphemeralPorts(OwnSid, It.IsAny<IReadOnlyList<PortRange>>()))
            .Callback(() =>
            {
                firstCallStarted.TrySetResult();
                releaseScan.Task.Wait();
            });

        var scanner = CreateScanner(
            MakeDatabase(new AccountEntry
            {
                Sid = OwnSid,
                Firewall = new FirewallAccountSettings
                {
                    AllowLocalhost = false,
                    FilterEphemeralLoopback = true,
                    LocalhostPortExemptions = ["49152-65535"]
                }
            }),
            new RecordingSnapshotProvider(new Dictionary<int, PortOwnerSet> { [55000] = Owners(OtherSid) }),
            blocker.Object);

        var firstTick = Task.Run(scanner.Start);
        await firstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        scanner.Start();
        releaseScan.TrySetResult();
        await firstTick;

        blocker.Verify(b => b.UpdateEphemeralPorts(OwnSid, It.IsAny<IReadOnlyList<PortRange>>()), Times.Once);
    }

    [Fact]
    public void Dispose_PreventsFutureScans()
    {
        var snapshotProvider = new RecordingSnapshotProvider(
            new Dictionary<int, PortOwnerSet> { [55000] = Owners(OtherSid) });
        var callCount = 0;
        var blocker = new Mock<IWfpLocalhostBlocker>();
        blocker
            .Setup(b => b.UpdateEphemeralPorts(OwnSid, It.IsAny<IReadOnlyList<PortRange>>()))
            .Callback(() => callCount++);

        using var scanner = CreateScanner(
            MakeDatabase(new AccountEntry
            {
                Sid = OwnSid,
                Firewall = new FirewallAccountSettings
                {
                    AllowLocalhost = false,
                    FilterEphemeralLoopback = true,
                    LocalhostPortExemptions = ["49152-65535"]
                }
            }),
            snapshotProvider,
            blocker.Object);

        scanner.Start();
        scanner.Dispose();
        scanner.Start();

        Assert.Equal(1, callCount);
    }

    private static AppDatabase MakeDatabase(params AccountEntry[] accounts) => new() { Accounts = [.. accounts] };

    private static (Mock<IWfpLocalhostBlocker> Blocker, WfpEphemeralPortScanner Scanner) CreateScanner(
        AppDatabase database,
        RecordingSnapshotProvider snapshotProvider)
    {
        var blocker = new Mock<IWfpLocalhostBlocker>();
        return (blocker, CreateScanner(database, snapshotProvider, blocker.Object));
    }

    private static WfpEphemeralPortScanner CreateScanner(
        AppDatabase database,
        IEphemeralPortOwnershipSnapshotProvider snapshotProvider,
        IWfpLocalhostBlocker blocker)
    {
        var dbAccessor = new UiThreadDatabaseAccessor(
            new LambdaDatabaseProvider(() => database),
            () => new InlineUiThreadInvoker(a => a()));
        return new WfpEphemeralPortScanner(blocker, dbAccessor, snapshotProvider, Mock.Of<ILoggingService>(), startTimer: false);
    }

    private sealed class RecordingSnapshotProvider(Dictionary<int, PortOwnerSet> snapshot)
        : IEphemeralPortOwnershipSnapshotProvider
    {
        public int CallCount { get; private set; }

        public Dictionary<int, PortOwnerSet> CollectListeningEphemeralPorts()
        {
            CallCount++;
            return snapshot;
        }
    }
}
