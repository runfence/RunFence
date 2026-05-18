using RunFence.Persistence;

namespace RunFence.Acl;

public class GrantIntentStoreSaveService : IGrantIntentStoreSaveService
{
    public void Save(
        IEnumerable<IGrantIntentStore> stores,
        GrantApplyFailureStep failureStep,
        string normalizedPath)
    {
        foreach (var store in stores
                     .Distinct()
                     .OrderBy(store => store.ConfigPath != null)
                     .ThenBy(store => store.ConfigPath, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                store.Save();
            }
            catch (Exception ex)
            {
                throw new GrantOperationException(
                    failureStep,
                    normalizedPath,
                    store.ConfigPath,
                    ex);
            }
        }
    }

    public IReadOnlyList<GrantApplyWarning> SaveWithWarnings(
        IEnumerable<IGrantIntentStore> stores,
        GrantApplyFailureStep failureStep,
        string normalizedPath)
    {
        try
        {
            Save(stores, failureStep, normalizedPath);
            return [];
        }
        catch (GrantOperationException ex)
        {
            return [new GrantApplyWarning(ex.Step, ex.Path ?? normalizedPath, ex.ConfigPath, ex.Cause)];
        }
    }

    public string? GetPrimaryConfigPath(IEnumerable<IGrantIntentStore> stores)
        => stores
            .Distinct()
            .OrderBy(store => store.ConfigPath != null)
            .ThenBy(store => store.ConfigPath, StringComparer.OrdinalIgnoreCase)
            .Select(store => store.ConfigPath)
            .FirstOrDefault();
}
