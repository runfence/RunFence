using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence.UI;

namespace RunFence.Persistence;

public class AdditionalGrantIntentStore(
    string configPath,
    List<AppConfigAccountEntry> accounts,
    ConfigSaveOrchestrator configSaveOrchestrator,
    GrantIntentOwnershipProjectionService ownershipProjection)
    : IGrantIntentStore
{
    public string ConfigPath { get; } = Path.GetFullPath(configPath);

    public IReadOnlyList<GrantedPathEntry> GetEntries(string ownerSid)
    {
        var entries = accounts.FirstOrDefault(account =>
            string.Equals(account.Sid, ownerSid, StringComparison.OrdinalIgnoreCase))?.Grants ?? [];
        return entries.Count == 0 ? [] : entries.Select(entry => entry.Clone()).ToList();
    }

    public void AddEntry(string ownerSid, GrantedPathEntry entry)
    {
        var clonedEntry = entry.Clone();
        var entries = GetWritableEntries(ownerSid, createIfMissing: true)!;
        entries.Add(clonedEntry);
        ownershipProjection.AddOwnership(ConfigPath, ownerSid, clonedEntry);
    }

    public bool RemoveEntry(string ownerSid, GrantedPathEntry entry)
    {
        var entries = GetWritableEntries(ownerSid, createIfMissing: false);
        if (entries == null)
            return false;

        var index = FindEntryIndex(entries, entry);
        if (index < 0)
            return false;

        var removedEntry = entries[index];
        entries.RemoveAt(index);
        RemoveAccountIfEmpty(ownerSid);

        ownershipProjection.RemoveOwnership(ConfigPath, ownerSid, removedEntry);
        return true;
    }

    public bool ReplaceEntry(string ownerSid, GrantedPathEntry existingEntry, GrantedPathEntry replacementEntry)
    {
        var entries = GetWritableEntries(ownerSid, createIfMissing: false);
        if (entries == null)
            return false;

        var index = FindEntryIndex(entries, existingEntry);
        if (index < 0)
            return false;

        var existing = entries[index];
        var replacement = replacementEntry.Clone();
        var replacementIndex = FindEntryIndex(entries, replacement);
        if (replacementIndex >= 0 && replacementIndex != index)
        {
            entries[replacementIndex] = replacement;
            entries.RemoveAt(index);
        }
        else
        {
            entries[index] = replacement;
        }

        ownershipProjection.RemoveOwnership(ConfigPath, ownerSid, existing);
        ownershipProjection.AddOwnership(ConfigPath, ownerSid, replacement);
        return true;
    }

    public void Save()
        => configSaveOrchestrator.SaveAdditionalConfig(
            ConfigPath,
            accounts);

    private List<GrantedPathEntry>? GetWritableEntries(string ownerSid, bool createIfMissing)
        => createIfMissing
            ? GetOrCreateAccount(ownerSid).Grants
            : accounts.FirstOrDefault(account =>
                string.Equals(account.Sid, ownerSid, StringComparison.OrdinalIgnoreCase))?.Grants;

    private AppConfigAccountEntry GetOrCreateAccount(string ownerSid)
    {
        var account = accounts.FirstOrDefault(existing =>
            string.Equals(existing.Sid, ownerSid, StringComparison.OrdinalIgnoreCase));
        if (account != null)
            return account;

        account = new AppConfigAccountEntry { Sid = ownerSid };
        accounts.Add(account);
        return account;
    }

    private void RemoveAccountIfEmpty(string ownerSid)
    {
        var account = accounts.FirstOrDefault(existing =>
            string.Equals(existing.Sid, ownerSid, StringComparison.OrdinalIgnoreCase));
        if (account is { Grants.Count: 0 })
            accounts.Remove(account);
    }

    private static int FindEntryIndex(List<GrantedPathEntry> entries, GrantedPathEntry entry)
        => entries.FindIndex(candidate =>
            GrantIntentEntryIdentity.From("", candidate) == GrantIntentEntryIdentity.From("", entry));
}
