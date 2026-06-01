using RunFence.Core.Models;
using RunFence.Launch;

namespace RunFence.Apps;

public readonly record struct AppEntryPathRepairResult(
    AppEntry App,
    bool Repaired,
    bool SaveFailed,
    string? WarningMessage);

public sealed class AppEntryPathRepairCoordinator(
    VersionedPathAutoRepairTrustPolicy trustPolicy,
    VersionedPathRepairer repairer,
    VersionedPathRepairOptionsBuilder optionsBuilder,
    AppEntryPathRepairCommitter committer)
{
    public AppEntryPathRepairResult RepairIfNeeded(AppEntry app)
    {
        if (!trustPolicy.TryCreateAutoRepairTrust(app, out var trust))
            return NoOp(app);

        var repair = repairer.TryRepair(app.ExePath, app.IsFolder, optionsBuilder.ForAutomaticRepair(trust));
        if (repair == null)
            return NoOp(app);

        return committer.Commit(app, repair.Value);
    }

    private static AppEntryPathRepairResult NoOp(AppEntry app)
        => new(app, Repaired: false, SaveFailed: false, WarningMessage: null);
}
