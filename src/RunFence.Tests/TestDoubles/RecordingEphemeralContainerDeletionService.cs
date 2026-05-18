using RunFence.Account.Lifecycle;
using RunFence.Core.Models;

namespace RunFence.Tests.TestDoubles;

public sealed class RecordingEphemeralContainerDeletionService : IContainerDeletionService
{
    private readonly List<string> _deletedContainerNames = [];

    public IReadOnlyList<string> DeletedContainerNames => _deletedContainerNames;

    public Func<AppContainerEntry, string?, ContainerDeletionResult> Delete { get; set; } =
        (entry, _) => ContainerDeletionResult.Success();

    public Action<AppDatabase, AppContainerEntry>? ApplyDeletionToDatabase { get; set; } =
        (database, entry) =>
        {
            database.AppContainers.Remove(entry);
            database.Apps.RemoveAll(app => app.AppContainerName == entry.Name);
        };

    public void Reset()
    {
        _deletedContainerNames.Clear();
        Delete = (entry, _) => ContainerDeletionResult.Success();
        ApplyDeletionToDatabase = (database, entry) =>
        {
            database.AppContainers.Remove(entry);
            database.Apps.RemoveAll(app => app.AppContainerName == entry.Name);
        };
    }

    public Task<ContainerDeletionResult> DeleteContainer(AppContainerEntry entry, string? containerSid)
    {
        _deletedContainerNames.Add(entry.Name);
        return Task.FromResult(Delete(entry, containerSid));
    }
}
