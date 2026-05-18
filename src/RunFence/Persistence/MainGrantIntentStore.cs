using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence.UI;

namespace RunFence.Persistence;

public class MainGrantIntentStore(
    ISessionProvider sessionProvider,
    ConfigSaveOrchestrator configSaveOrchestrator,
    GrantIntentOwnershipProjectionService ownershipProjection)
    : IGrantIntentStore
{
    public string? ConfigPath => null;

    public IReadOnlyList<GrantedPathEntry> GetEntries(string ownerSid)
    {
        var entries = sessionProvider.GetSession().Database.GetAccount(ownerSid)?.Grants ?? [];
        return entries.Count == 0
            ? []
            : entries
                .Where(entry =>
                    !ownershipProjection.HasRegisteredAdditionalConfigs ||
                    ownershipProjection.HasMainOwnership(ownerSid, entry))
                .Select(entry => entry.Clone())
                .ToList();
    }

    public void AddEntry(string ownerSid, GrantedPathEntry entry)
    {
        var entries = sessionProvider.GetSession().Database.GetOrCreateAccount(ownerSid).Grants;
        var entryIdentity = GrantIntentEntryIdentity.From(ownerSid, entry);
        var existingIndex = entries.FindIndex(candidate =>
            GrantIntentEntryIdentity.From(ownerSid, candidate) == entryIdentity);
        if (existingIndex >= 0)
        {
            var existing = entries[existingIndex];
            if (!ownershipProjection.HasMainOwnership(ownerSid, existing) &&
                ownershipProjection.HasAnyAdditionalOwnership(ownerSid, existing))
            {
                entries[existingIndex] = entry.Clone();
                existing = entries[existingIndex];
            }

            ownershipProjection.AddOwnership(configPath: null, ownerSid, existing);
            return;
        }

        var clonedEntry = entry.Clone();
        entries.Add(clonedEntry);
        ownershipProjection.AddOwnership(configPath: null, ownerSid, clonedEntry);
    }

    public bool RemoveEntry(string ownerSid, GrantedPathEntry entry)
    {
        var entries = sessionProvider.GetSession().Database.GetAccount(ownerSid)?.Grants;
        if (entries == null)
            return false;

        var index = FindEntryIndex(entries, entry);
        if (index < 0)
            return false;

        var removedEntry = entries[index];
        ownershipProjection.RemoveOwnership(configPath: null, ownerSid, removedEntry);
        if (ownershipProjection.HasAnyAdditionalOwnership(ownerSid, removedEntry))
        {
            entries[index] = ownershipProjection.GetAdditionalProjectionEntry(ownerSid, removedEntry) ?? removedEntry;
            return true;
        }

        entries.RemoveAt(index);
        sessionProvider.GetSession().Database.RemoveAccountIfEmpty(ownerSid);
        return true;
    }

    public bool ReplaceEntry(string ownerSid, GrantedPathEntry existingEntry, GrantedPathEntry replacementEntry)
    {
        var entries = sessionProvider.GetSession().Database.GetAccount(ownerSid)?.Grants;
        if (entries == null)
            return false;

        var index = FindEntryIndex(entries, existingEntry);
        if (index < 0)
            return false;

        var existing = entries[index];
        var replacement = replacementEntry.Clone();
        var keepExistingProjection = ownershipProjection.HasAnyAdditionalOwnership(ownerSid, existing);
        ownershipProjection.RemoveOwnership(configPath: null, ownerSid, existing);
        var replacementIndex = FindEntryIndex(entries, replacement);
        GrantedPathEntry projectedReplacement;
        if (keepExistingProjection)
        {
            if (replacementIndex >= 0)
            {
                entries[replacementIndex] = replacement;
                projectedReplacement = replacement;
            }
            else
            {
                entries.Add(replacement);
                projectedReplacement = replacement;
            }
        }
        else if (replacementIndex >= 0 && replacementIndex != index)
        {
            entries.RemoveAt(index);
            projectedReplacement = entries[replacementIndex > index ? replacementIndex - 1 : replacementIndex];
            sessionProvider.GetSession().Database.RemoveAccountIfEmpty(ownerSid);
        }
        else
        {
            entries[index] = replacement;
            projectedReplacement = replacement;
        }

        ownershipProjection.AddOwnership(configPath: null, ownerSid, projectedReplacement);
        return true;
    }

    public void Save()
        => configSaveOrchestrator.SaveMainConfig();

    private static int FindEntryIndex(List<GrantedPathEntry> entries, GrantedPathEntry entry)
        => entries.FindIndex(candidate =>
            GrantIntentEntryIdentity.From("", candidate) == GrantIntentEntryIdentity.From("", entry));
}
