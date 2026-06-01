using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Acl.UI;

public class AclApplySelectedStoreResolver(IGrantIntentStoreProvider grantIntentStoreProvider)
{
    public IGrantIntentStore? ResolveForGrantAdd(
        IReadOnlyDictionary<(string Path, bool IsDeny), string?> grantConfigMoves,
        GrantedPathEntry entry)
        => ResolveGrantStore(grantConfigMoves, entry.Path, entry.IsDeny);

    public IGrantIntentStore? ResolveForGrantModification(
        IReadOnlyDictionary<(string Path, bool IsDeny), string?> grantConfigMoves,
        PendingModification modification)
        => ResolveGrantStore(grantConfigMoves, modification.Entry.Path, modification.NewIsDeny)
           ?? ResolveGrantStore(grantConfigMoves, modification.Entry.Path, modification.Entry.IsDeny);

    public IGrantIntentStore? ResolveForTraverseAdd(
        IReadOnlyDictionary<string, string?> traverseConfigMoves,
        GrantedPathEntry entry)
    {
        var key = Path.GetFullPath(entry.Path);
        return traverseConfigMoves.TryGetValue(key, out var targetConfigPath)
            ? grantIntentStoreProvider.ResolveStore(targetConfigPath)
            : null;
    }

    private IGrantIntentStore? ResolveGrantStore(
        IReadOnlyDictionary<(string Path, bool IsDeny), string?> grantConfigMoves,
        string path,
        bool isDeny)
    {
        var key = (Path.GetFullPath(path), isDeny);
        return grantConfigMoves.TryGetValue(key, out var targetConfigPath)
            ? grantIntentStoreProvider.ResolveStore(targetConfigPath)
            : null;
    }
}
