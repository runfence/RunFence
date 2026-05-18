using RunFence.Acl;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Tests;

internal sealed class TestGrantIntentStore(string? configPath = null)
    : IGrantIntentStore
{
    private readonly Dictionary<string, List<GrantedPathEntry>> _entries = new(StringComparer.OrdinalIgnoreCase);
    private GrantIntentOwnershipProjectionService? _ownershipProjectionService;

    public string? ConfigPath { get; } = configPath == null ? null : Path.GetFullPath(configPath);

    public GrantIntentOwnershipProjectionService? OwnershipProjectionService
    {
        get => _ownershipProjectionService;
        set
        {
            _ownershipProjectionService = value;
            if (_ownershipProjectionService == null)
                return;

            foreach (var (ownerSid, entries) in _entries)
            {
                foreach (var entry in entries)
                    _ownershipProjectionService.AddOwnership(ConfigPath, ownerSid, entry);
            }
        }
    }

    public int SaveCount { get; private set; }

    public Action? SaveAction { get; set; }

    public IReadOnlyList<GrantedPathEntry> GetEntries(string ownerSid)
    {
        if (!_entries.TryGetValue(ownerSid, out var entries))
            return [];

        return entries
            .Where(entry =>
                ConfigPath != null ||
                OwnershipProjectionService?.HasRegisteredAdditionalConfigs != true ||
                OwnershipProjectionService.HasMainOwnership(ownerSid, entry))
            .Select(entry => entry.Clone())
            .ToList();
    }

    public void AddEntry(string ownerSid, GrantedPathEntry entry)
    {
        if (!_entries.TryGetValue(ownerSid, out var entries))
        {
            entries = [];
            _entries[ownerSid] = entries;
        }

        if (ConfigPath == null)
        {
            var entryIdentity = GrantIntentEntryIdentity.From(ownerSid, entry);
            var existingIndex = entries.FindIndex(candidate =>
                GrantIntentEntryIdentity.From(ownerSid, candidate) == entryIdentity);
            if (existingIndex >= 0)
            {
                var existing = entries[existingIndex];
                if (OwnershipProjectionService?.HasMainOwnership(ownerSid, existing) == false &&
                    OwnershipProjectionService.HasAnyAdditionalOwnership(ownerSid, existing))
                {
                    entries[existingIndex] = entry.Clone();
                    existing = entries[existingIndex];
                }

                OwnershipProjectionService?.AddOwnership(ConfigPath, ownerSid, existing);
                return;
            }
        }

        var clone = entry.Clone();
        entries.Add(clone);
        OwnershipProjectionService?.AddOwnership(ConfigPath, ownerSid, clone);
    }

    public bool RemoveEntry(string ownerSid, GrantedPathEntry entry)
    {
        if (!_entries.TryGetValue(ownerSid, out var entries))
            return false;

        var index = entries.FindIndex(candidate =>
            GrantIntentEntryIdentity.From(ownerSid, candidate) == GrantIntentEntryIdentity.From(ownerSid, entry));
        if (index < 0)
            return false;

        var removed = entries[index];
        OwnershipProjectionService?.RemoveOwnership(ConfigPath, ownerSid, removed);
        if (ConfigPath == null &&
            OwnershipProjectionService?.HasAnyAdditionalOwnership(ownerSid, removed) == true)
        {
            entries[index] = OwnershipProjectionService.GetAdditionalProjectionEntry(ownerSid, removed) ?? removed;
            return true;
        }

        entries.RemoveAt(index);
        if (entries.Count == 0)
            _entries.Remove(ownerSid);

        return true;
    }

    public bool ReplaceEntry(string ownerSid, GrantedPathEntry existingEntry, GrantedPathEntry replacementEntry)
    {
        if (!_entries.TryGetValue(ownerSid, out var entries))
            return false;

        var index = entries.FindIndex(candidate =>
            GrantIntentEntryIdentity.From(ownerSid, candidate) == GrantIntentEntryIdentity.From(ownerSid, existingEntry));
        if (index < 0)
            return false;

        var existing = entries[index];
        var replacement = replacementEntry.Clone();
        var keepExistingProjection = ConfigPath == null &&
            OwnershipProjectionService?.HasAnyAdditionalOwnership(ownerSid, existing) == true;
        OwnershipProjectionService?.RemoveOwnership(ConfigPath, ownerSid, existing);

        var replacementIndex = entries.FindIndex(candidate =>
            GrantIntentEntryIdentity.From(ownerSid, candidate) == GrantIntentEntryIdentity.From(ownerSid, replacement));
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
            if (entries.Count == 0)
                _entries.Remove(ownerSid);
        }
        else
        {
            entries[index] = replacement;
            projectedReplacement = replacement;
        }

        OwnershipProjectionService?.AddOwnership(ConfigPath, ownerSid, projectedReplacement);
        return true;
    }

    public void Save()
    {
        SaveCount++;
        SaveAction?.Invoke();
    }

}

internal sealed class RuntimeDatabaseGrantIntentStore(
    Func<AppDatabase> databaseAccessor,
    GrantIntentOwnershipProjectionService ownershipProjectionService)
    : IGrantIntentStore
{
    public string? ConfigPath => null;

    public IReadOnlyList<GrantedPathEntry> GetEntries(string ownerSid)
    {
        var entries = GetEntryViews(ownerSid);
        return entries
            .Where(entry => !ownershipProjectionService.HasRegisteredAdditionalConfigs ||
                            ownershipProjectionService.HasMainOwnership(ownerSid, entry))
            .Select(entry => entry.Clone())
            .ToList();
    }

    public void AddEntry(string ownerSid, GrantedPathEntry entry)
    {
        var entries = GetWritableEntries(ownerSid, entry, createIfMissing: true)!;
        var entryIdentity = GrantIntentEntryIdentity.From(ownerSid, entry);
        var existingIndex = entries.FindIndex(candidate =>
            GrantIntentEntryIdentity.From(ownerSid, candidate) == entryIdentity);
        if (existingIndex >= 0)
        {
            var existing = entries[existingIndex];
            if (!ownershipProjectionService.HasMainOwnership(ownerSid, existing) &&
                ownershipProjectionService.HasAnyAdditionalOwnership(ownerSid, existing))
            {
                entries[existingIndex] = entry.Clone();
                existing = entries[existingIndex];
            }

            ownershipProjectionService.AddOwnership(configPath: null, ownerSid, existing);
            return;
        }

        var clone = entry.Clone();
        entries.Add(clone);
        ownershipProjectionService.AddOwnership(configPath: null, ownerSid, clone);
    }

    public bool RemoveEntry(string ownerSid, GrantedPathEntry entry)
    {
        var entries = GetWritableEntries(ownerSid, entry, createIfMissing: false);
        if (entries == null)
            return false;

        var index = entries.FindIndex(candidate =>
            GrantIntentEntryIdentity.From(ownerSid, candidate) == GrantIntentEntryIdentity.From(ownerSid, entry));
        if (index < 0)
            return false;

        var removed = entries[index];
        ownershipProjectionService.RemoveOwnership(configPath: null, ownerSid, removed);
        if (ownershipProjectionService.HasAnyAdditionalOwnership(ownerSid, removed))
        {
            entries[index] = ownershipProjectionService.GetAdditionalProjectionEntry(ownerSid, removed) ?? removed;
            return true;
        }

        entries.RemoveAt(index);
        databaseAccessor().RemoveAccountIfEmpty(ownerSid);
        return true;
    }

    public bool ReplaceEntry(string ownerSid, GrantedPathEntry existingEntry, GrantedPathEntry replacementEntry)
    {
        var entries = GetWritableEntries(ownerSid, existingEntry, createIfMissing: false);
        if (entries == null)
            return false;

        var index = entries.FindIndex(candidate =>
            GrantIntentEntryIdentity.From(ownerSid, candidate) == GrantIntentEntryIdentity.From(ownerSid, existingEntry));
        if (index < 0)
            return false;

        var existing = entries[index];
        var replacement = replacementEntry.Clone();
        var keepExistingProjection = ownershipProjectionService.HasAnyAdditionalOwnership(ownerSid, existing);
        ownershipProjectionService.RemoveOwnership(configPath: null, ownerSid, existing);
        var targetEntries = GetWritableEntries(ownerSid, replacement, createIfMissing: true)!;
        var sameBucket = ReferenceEquals(entries, targetEntries);

        var replacementIndex = targetEntries.FindIndex(candidate =>
            GrantIntentEntryIdentity.From(ownerSid, candidate) == GrantIntentEntryIdentity.From(ownerSid, replacement));
        GrantedPathEntry projectedReplacement;
        if (keepExistingProjection)
        {
            if (replacementIndex >= 0)
            {
                targetEntries[replacementIndex] = replacement;
                projectedReplacement = replacement;
            }
            else
            {
                targetEntries.Add(replacement);
                projectedReplacement = replacement;
            }
        }
        else if (sameBucket && replacementIndex >= 0 && replacementIndex != index)
        {
            entries.RemoveAt(index);
            projectedReplacement = entries[replacementIndex > index ? replacementIndex - 1 : replacementIndex];
            databaseAccessor().RemoveAccountIfEmpty(ownerSid);
        }
        else
        {
            if (sameBucket)
            {
                entries[index] = replacement;
                projectedReplacement = replacement;
            }
            else
            {
                entries.RemoveAt(index);
                if (replacementIndex >= 0)
                {
                    targetEntries[replacementIndex] = replacement;
                    projectedReplacement = replacement;
                }
                else
                {
                    targetEntries.Add(replacement);
                    projectedReplacement = replacement;
                }

                databaseAccessor().RemoveAccountIfEmpty(ownerSid);
            }
        }

        ownershipProjectionService.AddOwnership(configPath: null, ownerSid, projectedReplacement);
        return true;
    }

    public void Save()
    {
    }

    private IReadOnlyList<GrantedPathEntry> GetEntryViews(string ownerSid)
        => databaseAccessor().GetAccount(ownerSid)?.Grants ?? [];

    private List<GrantedPathEntry>? GetWritableEntries(string ownerSid, GrantedPathEntry entry, bool createIfMissing)
    {
        if (!createIfMissing)
            return databaseAccessor().GetAccount(ownerSid)?.Grants;

        return databaseAccessor().GetOrCreateAccount(ownerSid).Grants;
    }
}

internal sealed class TestGrantIntentStoreProvider : IGrantIntentStoreProvider
{
    private readonly Dictionary<string, TestGrantIntentStore> _additionalStores = new(StringComparer.OrdinalIgnoreCase);
    private readonly IGrantIntentStore _mainStore;
    public GrantIntentOwnershipProjectionService OwnershipProjectionService { get; }

    public IGrantIntentStore MainStore => _mainStore;

    public TestGrantIntentStoreProvider(
        IGrantIntentStore mainStore,
        GrantIntentOwnershipProjectionService? ownershipProjectionService = null)
    {
        _mainStore = mainStore;
        OwnershipProjectionService = ownershipProjectionService ?? new GrantIntentOwnershipProjectionService();
        if (mainStore is TestGrantIntentStore testStore)
            testStore.OwnershipProjectionService = OwnershipProjectionService;
    }

    public IReadOnlyList<IGrantIntentStore> GetLoadedStores()
        => [_mainStore, .. _additionalStores.Values.OrderBy(store => store.ConfigPath, StringComparer.OrdinalIgnoreCase)];

    public IGrantIntentStore ResolveStore(string? configPath)
    {
        if (configPath == null)
            return _mainStore;

        var normalizedPath = Path.GetFullPath(configPath);
        if (_additionalStores.TryGetValue(normalizedPath, out var store))
            return store;

        throw new InvalidOperationException($"Grant intent store is not loaded for '{normalizedPath}'.");
    }

    public IGrantIntentStore RegisterAdditionalStore(
        string configPath,
        List<AppConfigAccountEntry> accounts)
    {
        throw new NotSupportedException();
    }

    public void UnregisterAdditionalStore(string configPath)
    {
        var normalizedPath = Path.GetFullPath(configPath);
        _additionalStores.Remove(normalizedPath);
        OwnershipProjectionService.UnregisterAdditionalConfig(normalizedPath);
    }

    public void AddLoadedStore(TestGrantIntentStore store)
    {
        if (store.ConfigPath == null)
            throw new InvalidOperationException("Additional test stores must have a config path.");

        store.OwnershipProjectionService = OwnershipProjectionService;
        _additionalStores[store.ConfigPath] = store;
    }
}
