using RunFence.Launching.Processes;
using RunFence.ProfileKeeper;
using Xunit;

namespace RunFence.Tests;

public sealed class ProfileKeeperRunnerTests
{
    private const string Sid = "S-1-5-21-100-200-300-1001";
    private const string KeeperPath = @"C:\RunFence\RunFence.ProfileKeeper.exe";

    [Fact]
    public void Run_WhenFirstScanShowsObsoleteButRevalidationShowsNewest_DoesNotExitImmediately()
    {
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero));
        var snapshotReader = new SequenceProcessSnapshotReader(
            timeProvider,
            TimeSpan.FromSeconds(30),
            [
                [new ProcessSnapshotInfo(10, Sid, KeeperPath, null), new ProcessSnapshotInfo(20, Sid, KeeperPath, null)],
                [new ProcessSnapshotInfo(10, Sid, KeeperPath, null)],
                [new ProcessSnapshotInfo(10, Sid, KeeperPath, null)],
                [new ProcessSnapshotInfo(10, Sid, KeeperPath, null)],
            ]);
        var runner = new ProfileKeeperRunner(
            new ProfileKeeperIdentity(10, Sid, KeeperPath),
            new ProfileKeeperOptions(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            snapshotReader,
            new ProfileKeeperStateEvaluator(),
            new RecordingProcessTerminator(),
            timeProvider);

        runner.Run(CancellationToken.None);

        Assert.True(snapshotReader.ReadCount >= 3);
    }

    [Fact]
    public void Run_WhenOnlyKeepersRemain_ExitsAfterGracePeriod()
    {
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero));
        var snapshotReader = new SequenceProcessSnapshotReader(
            timeProvider,
            TimeSpan.FromSeconds(30),
            [
                [new ProcessSnapshotInfo(10, Sid, KeeperPath, null)],
                [new ProcessSnapshotInfo(10, Sid, KeeperPath, null)],
                [new ProcessSnapshotInfo(10, Sid, KeeperPath, null)],
            ]);
        var runner = new ProfileKeeperRunner(
            new ProfileKeeperIdentity(10, Sid, KeeperPath),
            new ProfileKeeperOptions(TimeSpan.Zero, TimeSpan.FromSeconds(60)),
            snapshotReader,
            new ProfileKeeperStateEvaluator(),
            new RecordingProcessTerminator(),
            timeProvider);

        runner.Run(CancellationToken.None);

        Assert.Equal(3, snapshotReader.ReadCount);
    }

    [Fact]
    public void Run_WhenOnlyIgnoredLingeringProcessRemains_TerminatesItAndRechecksBeforeExit()
    {
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero));
        var snapshotReader = new SequenceProcessSnapshotReader(
            timeProvider,
            TimeSpan.FromMilliseconds(500),
            [
                [
                    new ProcessSnapshotInfo(10, Sid, KeeperPath, null),
                    new ProcessSnapshotInfo(44, Sid, Path.Combine(Environment.SystemDirectory, "conhost.exe"), null),
                ],
                [
                    new ProcessSnapshotInfo(10, Sid, KeeperPath, null),
                    new ProcessSnapshotInfo(44, Sid, Path.Combine(Environment.SystemDirectory, "conhost.exe"), null),
                ],
                [new ProcessSnapshotInfo(10, Sid, KeeperPath, null)],
            ]);
        var terminator = new RecordingProcessTerminator();
        var runner = new ProfileKeeperRunner(
            new ProfileKeeperIdentity(10, Sid, KeeperPath),
            new ProfileKeeperOptions(TimeSpan.Zero, TimeSpan.Zero),
            snapshotReader,
            new ProfileKeeperStateEvaluator(),
            terminator,
            timeProvider);

        runner.Run(CancellationToken.None);

        Assert.Equal([44], terminator.TerminatedProcessIds);
        Assert.True(snapshotReader.ReadCount >= 3);
    }

    [Fact]
    public void Run_WhenCleanupRecheckFindsNewRealProcess_DoesNotExitUntilIdleRestarts()
    {
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero));
        var snapshotReader = new SequenceProcessSnapshotReader(
            timeProvider,
            TimeSpan.FromSeconds(30),
            [
                [
                    new ProcessSnapshotInfo(10, Sid, KeeperPath, null),
                    new ProcessSnapshotInfo(44, Sid, Path.Combine(Environment.SystemDirectory, "conhost.exe"), null),
                ],
                [
                    new ProcessSnapshotInfo(10, Sid, KeeperPath, null),
                    new ProcessSnapshotInfo(44, Sid, Path.Combine(Environment.SystemDirectory, "conhost.exe"), null),
                ],
                [
                    new ProcessSnapshotInfo(10, Sid, KeeperPath, null),
                    new ProcessSnapshotInfo(44, Sid, Path.Combine(Environment.SystemDirectory, "conhost.exe"), null),
                ],
                [
                    new ProcessSnapshotInfo(10, Sid, KeeperPath, null),
                    new ProcessSnapshotInfo(50, Sid, @"C:\Apps\App.exe", null),
                ],
                [new ProcessSnapshotInfo(10, Sid, KeeperPath, null)],
                [new ProcessSnapshotInfo(10, Sid, KeeperPath, null)],
                [new ProcessSnapshotInfo(10, Sid, KeeperPath, null)],
            ]);
        var terminator = new RecordingProcessTerminator();
        var runner = new ProfileKeeperRunner(
            new ProfileKeeperIdentity(10, Sid, KeeperPath),
            new ProfileKeeperOptions(TimeSpan.Zero, TimeSpan.FromSeconds(60)),
            snapshotReader,
            new ProfileKeeperStateEvaluator(),
            terminator,
            timeProvider);

        runner.Run(CancellationToken.None);

        Assert.Equal([44], terminator.TerminatedProcessIds);
        Assert.True(snapshotReader.ReadCount >= 7);
    }

    [Fact]
    public void Run_WhenIgnoredLingeringProcessStillRemainsAfterCleanup_DoesNotExit()
    {
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero));
        var snapshotReader = new SequenceProcessSnapshotReader(
            timeProvider,
            TimeSpan.FromSeconds(2),
            [
                [
                    new ProcessSnapshotInfo(10, Sid, KeeperPath, null),
                    new ProcessSnapshotInfo(44, Sid, Path.Combine(Environment.SystemDirectory, "conhost.exe"), null),
                ],
                [
                    new ProcessSnapshotInfo(10, Sid, KeeperPath, null),
                    new ProcessSnapshotInfo(44, Sid, Path.Combine(Environment.SystemDirectory, "conhost.exe"), null),
                ],
                [
                    new ProcessSnapshotInfo(10, Sid, KeeperPath, null),
                    new ProcessSnapshotInfo(44, Sid, Path.Combine(Environment.SystemDirectory, "conhost.exe"), null),
                ],
                [
                    new ProcessSnapshotInfo(10, Sid, KeeperPath, null),
                    new ProcessSnapshotInfo(44, Sid, Path.Combine(Environment.SystemDirectory, "conhost.exe"), null),
                ],
                [
                    new ProcessSnapshotInfo(10, Sid, KeeperPath, null),
                    new ProcessSnapshotInfo(44, Sid, Path.Combine(Environment.SystemDirectory, "conhost.exe"), null),
                ],
                [
                    new ProcessSnapshotInfo(10, Sid, KeeperPath, null),
                    new ProcessSnapshotInfo(44, Sid, Path.Combine(Environment.SystemDirectory, "conhost.exe"), null),
                ],
                [new ProcessSnapshotInfo(10, Sid, KeeperPath, null)],
                [new ProcessSnapshotInfo(10, Sid, KeeperPath, null)],
                [new ProcessSnapshotInfo(10, Sid, KeeperPath, null)],
                [new ProcessSnapshotInfo(10, Sid, KeeperPath, null)],
            ]);
        var terminator = new RecordingProcessTerminator();
        var runner = new ProfileKeeperRunner(
            new ProfileKeeperIdentity(10, Sid, KeeperPath),
            new ProfileKeeperOptions(TimeSpan.Zero, TimeSpan.FromSeconds(4)),
            snapshotReader,
            new ProfileKeeperStateEvaluator(),
            terminator,
            timeProvider);

        runner.Run(CancellationToken.None);

        Assert.Equal([44], terminator.TerminatedProcessIds);
        Assert.True(snapshotReader.ReadCount > 6);
    }

    private sealed class SequenceProcessSnapshotReader(
        MutableTimeProvider timeProvider,
        TimeSpan advancePerRead,
        IReadOnlyList<IReadOnlyList<ProcessSnapshotInfo>> snapshots)
        : IProcessSnapshotReader
    {
        public int ReadCount { get; private set; }

        public IReadOnlyList<ProcessSnapshotInfo> GetProcesses()
        {
            var index = Math.Min(ReadCount, snapshots.Count - 1);
            ReadCount++;
            timeProvider.Advance(advancePerRead);
            return snapshots[index];
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan amount)
        {
            _utcNow += amount;
        }
    }

    private sealed class RecordingProcessTerminator : IProfileKeeperProcessTerminator
    {
        public List<int> TerminatedProcessIds { get; } = [];

        public void Terminate(int processId)
        {
            TerminatedProcessIds.Add(processId);
        }
    }
}
