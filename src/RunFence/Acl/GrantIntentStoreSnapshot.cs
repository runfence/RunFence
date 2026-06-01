using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Acl;

public sealed class GrantIntentStoreSnapshot
{
    public GrantIntentStoreSnapshot(
        IGrantIntentStore store,
        string ownerSid,
        string targetPath,
        string? traversePath,
        bool includeDeny,
        IReadOnlyList<GrantedPathEntry> entries)
    {
        Store = store;
        OwnerSid = ownerSid;
        TargetPath = Path.GetFullPath(targetPath);
        TraversePath = string.IsNullOrEmpty(traversePath) ? null : Path.GetFullPath(traversePath);
        IncludeDeny = includeDeny;
        Entries = entries.Select(entry => entry.Clone()).ToList();
    }

    public IGrantIntentStore Store { get; }

    public string OwnerSid { get; }

    public string TargetPath { get; }

    public string? TraversePath { get; }

    public bool IncludeDeny { get; }

    public IReadOnlyList<GrantedPathEntry> Entries { get; }
}
