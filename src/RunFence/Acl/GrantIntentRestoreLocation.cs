using RunFence.Core.Models;

namespace RunFence.Acl;

public sealed class GrantIntentRestoreLocation
{
    public GrantIntentRestoreLocation(GrantIntentStoreIdentity storeIdentity, GrantedPathEntry entry)
    {
        StoreIdentity = new GrantIntentStoreIdentity(storeIdentity.ConfigPath);
        Entry = entry.Clone();
    }

    public GrantIntentStoreIdentity StoreIdentity { get; }

    public GrantedPathEntry Entry { get; }
}
