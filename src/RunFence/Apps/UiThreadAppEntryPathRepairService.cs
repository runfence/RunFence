using RunFence.Infrastructure;

namespace RunFence.Apps;

public sealed class UiThreadAppEntryPathRepairService(
    UiThreadDatabaseAccessor databaseAccessor,
    AppEntryPathRepairCoordinator coordinator)
{
    public AppEntryPathRepairResult RepairIfNeeded(string appId)
    {
        return databaseAccessor.Write(database =>
        {
            var app = database.Apps.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, appId, StringComparison.OrdinalIgnoreCase));
            if (app == null)
                throw new InvalidOperationException($"The application '{appId}' no longer exists.");

            return coordinator.RepairIfNeeded(app);
        });
    }
}
