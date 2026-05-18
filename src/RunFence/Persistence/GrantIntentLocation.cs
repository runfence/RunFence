using RunFence.Core.Models;

namespace RunFence.Persistence;

public sealed class GrantIntentLocation
{
    public GrantIntentLocation(GrantedPathEntry entry, IGrantIntentStore store)
    {
        Entry = entry.Clone();
        Store = store;
    }

    public GrantedPathEntry Entry { get; }

    public IGrantIntentStore Store { get; }
}
