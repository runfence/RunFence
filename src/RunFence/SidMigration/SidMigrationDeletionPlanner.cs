using RunFence.Core.Models;

namespace RunFence.SidMigration;

public sealed class SidMigrationDeletionPlanner
{
    public SidMigrationDeletionPlan BuildPlan(
        IReadOnlyList<string> sidsToDelete,
        AppDatabase snapshot)
    {
        ArgumentNullException.ThrowIfNull(sidsToDelete);
        ArgumentNullException.ThrowIfNull(snapshot);

        var plannedSids = sidsToDelete.ToList().AsReadOnly();
        var appsNeedingShortcutCleanup = snapshot.Apps
            .Where(app =>
                app.ManageShortcuts
                && plannedSids.Any(sid => string.Equals(app.AccountSid, sid, StringComparison.OrdinalIgnoreCase)))
            .ToList()
            .AsReadOnly();

        return new SidMigrationDeletionPlan(plannedSids, appsNeedingShortcutCleanup);
    }
}

public sealed record SidMigrationDeletionPlan(
    IReadOnlyList<string> SidsToDelete,
    IReadOnlyList<AppEntry> AppsNeedingShortcutCleanup);
