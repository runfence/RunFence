using RunFence.Core.Models;

namespace RunFence.Persistence;

public interface IGrantIntentStore
{
    string? ConfigPath { get; }

    IReadOnlyList<GrantedPathEntry> GetEntries(string ownerSid);

    void AddEntry(string ownerSid, GrantedPathEntry entry);

    bool RemoveEntry(string ownerSid, GrantedPathEntry entry);

    bool ReplaceEntry(string ownerSid, GrantedPathEntry existingEntry, GrantedPathEntry replacementEntry);

    void Save();
}
