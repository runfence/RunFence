namespace RunFence.Persistence.UI;

public interface IHandlerSyncService
{
    void Sync();

    void Sync(IReadOnlyCollection<string>? removedAssociationKeys);
}
