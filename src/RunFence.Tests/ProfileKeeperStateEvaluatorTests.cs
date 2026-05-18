using RunFence.Launching.Processes;
using RunFence.ProfileKeeper;
using Xunit;

namespace RunFence.Tests;

public sealed class ProfileKeeperStateEvaluatorTests
{
    private const string Sid = "S-1-5-21-100-200-300-1001";
    private const string KeeperPath = @"C:\RunFence\RunFence.ProfileKeeper.exe";

    [Fact]
    public void Evaluate_WhenHigherPidKeeperExists_MarksCurrentKeeperObsolete()
    {
        var evaluator = new ProfileKeeperStateEvaluator();
        var identity = new ProfileKeeperIdentity(100, Sid, KeeperPath);

        var result = evaluator.Evaluate(identity,
        [
            new ProcessSnapshotInfo(100, Sid, KeeperPath, null),
            new ProcessSnapshotInfo(250, Sid, KeeperPath, null),
        ]);

        Assert.False(result.IsNewestKeeper);
        Assert.Equal(0, result.SameSidBlockingProcessCount);
        Assert.Empty(result.IgnorableProcessIds);
    }

    [Fact]
    public void Evaluate_WhenCurrentKeeperHasNewerCreationTime_IgnoresHigherPidOlderKeeper()
    {
        var evaluator = new ProfileKeeperStateEvaluator();
        var identity = new ProfileKeeperIdentity(100, Sid, KeeperPath);

        var result = evaluator.Evaluate(identity,
        [
            new ProcessSnapshotInfo(100, Sid, KeeperPath, 2_000),
            new ProcessSnapshotInfo(250, Sid, KeeperPath, 1_000),
        ]);

        Assert.True(result.IsNewestKeeper);
        Assert.Equal(0, result.SameSidBlockingProcessCount);
        Assert.Empty(result.IgnorableProcessIds);
    }

    [Fact]
    public void Evaluate_WhenKeeperCreationTimesMatch_UsesHigherPidAsTieBreaker()
    {
        var evaluator = new ProfileKeeperStateEvaluator();
        var identity = new ProfileKeeperIdentity(100, Sid, KeeperPath);

        var result = evaluator.Evaluate(identity,
        [
            new ProcessSnapshotInfo(100, Sid, KeeperPath, 2_000),
            new ProcessSnapshotInfo(250, Sid, KeeperPath, 2_000),
        ]);

        Assert.False(result.IsNewestKeeper);
        Assert.Equal(0, result.SameSidBlockingProcessCount);
        Assert.Empty(result.IgnorableProcessIds);
    }

    [Fact]
    public void Evaluate_WhenSameSidDifferentProcessExists_TracksActivityWithoutCountingOtherKeepers()
    {
        var evaluator = new ProfileKeeperStateEvaluator();
        var identity = new ProfileKeeperIdentity(300, Sid, KeeperPath);

        var result = evaluator.Evaluate(identity,
        [
            new ProcessSnapshotInfo(300, Sid, KeeperPath, null),
            new ProcessSnapshotInfo(250, Sid, KeeperPath, null),
            new ProcessSnapshotInfo(200, Sid, @"C:\Apps\App.exe", null),
        ]);

        Assert.True(result.IsNewestKeeper);
        Assert.Equal(1, result.SameSidBlockingProcessCount);
        Assert.Empty(result.IgnorableProcessIds);
    }

    [Fact]
    public void Evaluate_WhenOnlyKeepersRemain_DoesNotTreatThemAsActivity()
    {
        var evaluator = new ProfileKeeperStateEvaluator();
        var identity = new ProfileKeeperIdentity(400, Sid, KeeperPath);

        var result = evaluator.Evaluate(identity,
        [
            new ProcessSnapshotInfo(400, Sid, KeeperPath, null),
            new ProcessSnapshotInfo(350, Sid, KeeperPath, null),
        ]);

        Assert.True(result.IsNewestKeeper);
        Assert.Equal(0, result.SameSidBlockingProcessCount);
        Assert.Empty(result.IgnorableProcessIds);
    }

    [Fact]
    public void Evaluate_WhenOnlyIgnoredLingeringProcessRemains_TreatsItAsIgnorable()
    {
        var evaluator = new ProfileKeeperStateEvaluator();
        var identity = new ProfileKeeperIdentity(400, Sid, KeeperPath);

        var result = evaluator.Evaluate(identity,
        [
            new ProcessSnapshotInfo(400, Sid, KeeperPath, null),
            new ProcessSnapshotInfo(450, Sid, Path.Combine(Environment.SystemDirectory, "conhost.exe"), null),
        ]);

        Assert.True(result.IsNewestKeeper);
        Assert.Equal(0, result.SameSidBlockingProcessCount);
        Assert.Equal([450], result.IgnorableProcessIds);
    }
}
