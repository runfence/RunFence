using RunFence.Acl;
using RunFence.Acl.UI;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class AclApplySelectedStoreResolverTests
{
    [Fact]
    public void ResolveForGrantAdd_ReturnsSelectedStoreForMatchingMove()
    {
        var provider = new TestGrantIntentStoreProvider(new TestGrantIntentStore());
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra.rfn");
        provider.AddLoadedStore(additionalStore);
        var resolver = new AclApplySelectedStoreResolver(provider);
        var entry = new GrantedPathEntry { Path = @"C:\Grant", IsDeny = false };
        var moves = new Dictionary<(string Path, bool IsDeny), string?>(new GrantPathKeyComparer())
        {
            [(Path.GetFullPath(entry.Path), entry.IsDeny)] = additionalStore.ConfigPath
        };

        var selectedStore = resolver.ResolveForGrantAdd(moves, entry);

        Assert.Same(additionalStore, selectedStore);
    }

    [Fact]
    public void ResolveForGrantModification_PrefersNewModeKeyAndFallsBackToCommittedKey()
    {
        var provider = new TestGrantIntentStoreProvider(new TestGrantIntentStore());
        var newModeStore = new TestGrantIntentStore(@"C:\Configs\new-mode.rfn");
        var committedStore = new TestGrantIntentStore(@"C:\Configs\committed.rfn");
        provider.AddLoadedStore(newModeStore);
        provider.AddLoadedStore(committedStore);
        var resolver = new AclApplySelectedStoreResolver(provider);
        var entry = new GrantedPathEntry { Path = @"C:\Switch", IsDeny = false };
        var modification = new PendingModification(entry, WasIsDeny: false, WasOwn: false, NewIsDeny: true, NewRights: null);

        var newModeMoves = new Dictionary<(string Path, bool IsDeny), string?>(new GrantPathKeyComparer())
        {
            [(Path.GetFullPath(entry.Path), true)] = newModeStore.ConfigPath,
            [(Path.GetFullPath(entry.Path), false)] = committedStore.ConfigPath
        };
        Assert.Same(newModeStore, resolver.ResolveForGrantModification(newModeMoves, modification));

        var committedMoves = new Dictionary<(string Path, bool IsDeny), string?>(new GrantPathKeyComparer())
        {
            [(Path.GetFullPath(entry.Path), false)] = committedStore.ConfigPath
        };
        Assert.Same(committedStore, resolver.ResolveForGrantModification(committedMoves, modification));
    }

    [Fact]
    public void ResolveForTraverseAdd_ReturnsNullWhenMoveIsMissing()
    {
        var provider = new TestGrantIntentStoreProvider(new TestGrantIntentStore());
        var resolver = new AclApplySelectedStoreResolver(provider);
        var entry = new GrantedPathEntry { Path = @"C:\Traverse", IsTraverseOnly = true };

        var selectedStore = resolver.ResolveForTraverseAdd(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase), entry);

        Assert.Null(selectedStore);
    }
}
