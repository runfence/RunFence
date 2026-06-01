using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Persistence;

public class GrantIntentOwnershipProjectionService
{
    private readonly HashSet<GrantIntentEntryIdentity> _mainOwnership = [];
    private readonly Dictionary<string, HashSet<GrantIntentEntryIdentity>> _additionalOwnership =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<GrantIntentEntryIdentity, GrantedPathEntry>> _additionalProjectionEntries =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _mainOwnershipCaptured;

    public bool HasRegisteredAdditionalConfigs => _additionalOwnership.Count > 0;

    internal GrantIntentOwnershipProjectionSnapshot CaptureSnapshot()
        => new(
            new HashSet<GrantIntentEntryIdentity>(_mainOwnership),
            _additionalOwnership.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlySet<GrantIntentEntryIdentity>)new HashSet<GrantIntentEntryIdentity>(kvp.Value),
                StringComparer.OrdinalIgnoreCase),
            _additionalProjectionEntries.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyDictionary<GrantIntentEntryIdentity, GrantedPathEntry>)kvp.Value.ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value.Clone()),
                StringComparer.OrdinalIgnoreCase),
            _mainOwnershipCaptured);

    internal void RestoreSnapshot(GrantIntentOwnershipProjectionSnapshot snapshot)
    {
        _mainOwnership.Clear();
        foreach (var identity in snapshot.MainOwnership)
            _mainOwnership.Add(identity);

        _additionalOwnership.Clear();
        foreach (var (configPath, ownership) in snapshot.AdditionalOwnership)
            _additionalOwnership[configPath] = new HashSet<GrantIntentEntryIdentity>(ownership);

        _additionalProjectionEntries.Clear();
        foreach (var (configPath, projectionEntries) in snapshot.AdditionalProjectionEntries)
        {
            _additionalProjectionEntries[configPath] = projectionEntries.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.Clone());
        }

        _mainOwnershipCaptured = snapshot.MainOwnershipCaptured;
    }

    public void CaptureMainOwnershipBaseline(AppDatabase database)
    {
        if (_mainOwnershipCaptured)
            return;

        ReplaceMainOwnership(database.Accounts);
    }

    public void ReplaceMainOwnership(IEnumerable<AccountEntry> accounts)
    {
        _mainOwnership.Clear();

        foreach (var account in accounts)
        {
            foreach (var entry in account.Grants)
                _mainOwnership.Add(GrantIntentEntryIdentity.From(account.Sid, entry));
        }

        _mainOwnershipCaptured = true;
    }

    public void RegisterAdditionalConfig(
        string configPath,
        IEnumerable<AppConfigAccountEntry>? accounts)
    {
        var normalizedConfigPath = NormalizeConfigPath(configPath);
        var ownership = new HashSet<GrantIntentEntryIdentity>();
        var projectionEntries = new Dictionary<GrantIntentEntryIdentity, GrantedPathEntry>();

        if (accounts != null)
        {
            foreach (var account in accounts)
            {
                foreach (var entry in account.Grants)
                    AddAdditionalProjectionEntry(ownership, projectionEntries, account.Sid, entry);
            }
        }

        _additionalOwnership[normalizedConfigPath] = ownership;
        _additionalProjectionEntries[normalizedConfigPath] = projectionEntries;
    }

    public void UnregisterAdditionalConfig(string configPath)
    {
        var normalizedConfigPath = NormalizeConfigPath(configPath);
        _additionalOwnership.Remove(normalizedConfigPath);
        _additionalProjectionEntries.Remove(normalizedConfigPath);
    }

    public void AddOwnership(string? configPath, string ownerSid, GrantedPathEntry entry)
    {
        var identity = GrantIntentEntryIdentity.From(ownerSid, entry);
        if (configPath == null)
        {
            _mainOwnership.Add(identity);
            return;
        }

        GetOrCreateAdditionalOwnership(configPath).Add(identity);
        GetOrCreateAdditionalProjectionEntries(configPath)[identity] = entry.Clone();
    }

    public void RemoveOwnership(string? configPath, string ownerSid, GrantedPathEntry entry)
    {
        var identity = GrantIntentEntryIdentity.From(ownerSid, entry);
        if (configPath == null)
        {
            _mainOwnership.Remove(identity);
            return;
        }

        if (!_additionalOwnership.TryGetValue(NormalizeConfigPath(configPath), out var ownership))
            return;

        ownership.Remove(identity);
        if (_additionalProjectionEntries.TryGetValue(NormalizeConfigPath(configPath), out var projectionEntries))
            projectionEntries.Remove(identity);
    }

    public bool HasMainOwnership(string ownerSid, GrantedPathEntry entry)
        => _mainOwnership.Contains(GrantIntentEntryIdentity.From(ownerSid, entry));

    public bool HasAdditionalOwnership(string configPath, string ownerSid, GrantedPathEntry entry)
        => _additionalOwnership.TryGetValue(NormalizeConfigPath(configPath), out var ownership) &&
           ownership.Contains(GrantIntentEntryIdentity.From(ownerSid, entry));

    public bool HasOwnershipOutsideConfig(string unloadingConfigPath, string ownerSid, GrantedPathEntry entry)
    {
        if (HasMainOwnership(ownerSid, entry))
            return true;

        var normalizedConfigPath = NormalizeConfigPath(unloadingConfigPath);
        var identity = GrantIntentEntryIdentity.From(ownerSid, entry);
        return _additionalOwnership.Any(kvp =>
            !string.Equals(kvp.Key, normalizedConfigPath, StringComparison.OrdinalIgnoreCase) &&
            kvp.Value.Contains(identity));
    }

    public bool HasAnyAdditionalOwnership(string ownerSid, GrantedPathEntry entry)
    {
        var identity = GrantIntentEntryIdentity.From(ownerSid, entry);
        return _additionalOwnership.Values.Any(ownership => ownership.Contains(identity));
    }

    public GrantedPathEntry? GetAdditionalProjectionEntry(string ownerSid, GrantedPathEntry entry)
    {
        var identity = GrantIntentEntryIdentity.From(ownerSid, entry);
        foreach (var projectionEntries in _additionalProjectionEntries
                     .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                     .Select(kvp => kvp.Value))
        {
            if (projectionEntries.TryGetValue(identity, out var projectionEntry))
                return projectionEntry.Clone();
        }

        return null;
    }

    private HashSet<GrantIntentEntryIdentity> GetOrCreateAdditionalOwnership(string configPath)
    {
        var normalizedConfigPath = NormalizeConfigPath(configPath);
        if (_additionalOwnership.TryGetValue(normalizedConfigPath, out var ownership))
            return ownership;

        ownership = [];
        _additionalOwnership[normalizedConfigPath] = ownership;
        return ownership;
    }

    private Dictionary<GrantIntentEntryIdentity, GrantedPathEntry> GetOrCreateAdditionalProjectionEntries(
        string configPath)
    {
        var normalizedConfigPath = NormalizeConfigPath(configPath);
        if (_additionalProjectionEntries.TryGetValue(normalizedConfigPath, out var projectionEntries))
            return projectionEntries;

        projectionEntries = [];
        _additionalProjectionEntries[normalizedConfigPath] = projectionEntries;
        return projectionEntries;
    }

    private static void AddAdditionalProjectionEntry(
        ISet<GrantIntentEntryIdentity> ownership,
        IDictionary<GrantIntentEntryIdentity, GrantedPathEntry> projectionEntries,
        string ownerSid,
        GrantedPathEntry entry)
    {
        var identity = GrantIntentEntryIdentity.From(ownerSid, entry);
        ownership.Add(identity);
        projectionEntries[identity] = entry.Clone();
    }

    private static string NormalizeConfigPath(string configPath)
        => AppConfigPathHelper.NormalizePath(configPath);
}
