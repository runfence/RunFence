using RunFence.Persistence;

namespace RunFence.Acl;

public interface IGrantIntentStoreSaveService
{
    void Save(
        IEnumerable<IGrantIntentStore> stores,
        GrantApplyFailureStep failureStep,
        string normalizedPath);

    IReadOnlyList<GrantApplyWarning> SaveWithWarnings(
        IEnumerable<IGrantIntentStore> stores,
        GrantApplyFailureStep failureStep,
        string normalizedPath);

    string? GetPrimaryConfigPath(IEnumerable<IGrantIntentStore> stores);
}
