using System.Text.Json.Serialization;
using static System.StringComparison;

namespace RunFence.Core.Models;

public class AppDatabase
{
    public int Version { get; set; } = 2;
    public List<AppEntry> Apps { get; set; } = new();
    public List<AccountEntry> Accounts { get; set; } = new();
    public List<AppContainerEntry> AppContainers { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
    public string? LastPrefsFilePath { get; set; }

    /// <summary>
    /// Central SID-to-username map. Entries are never deleted — this is the permanent
    /// record of all known SID-to-name associations.
    /// </summary>
    [JsonConverter(typeof(SidNamesDictionaryConverter))]
    public Dictionary<string, string> SidNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Snapshot of each known SID's local group memberships at the last reconciliation check.
    /// Used to detect group membership changes between sessions and re-apply traverse ACEs
    /// when a user is added to or removed from a group. Keys are SIDs (case-insensitive).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, List<string>>? AccountGroupSnapshots { get; set; }

    public AccountEntry? GetAccount(string sid)
        => Accounts.FirstOrDefault(a => string.Equals(a.Sid, sid, OrdinalIgnoreCase));

    public AccountEntry GetOrCreateAccount(string sid)
    {
        var entry = GetAccount(sid);
        if (entry != null)
            return entry;
        var newEntry = new AccountEntry { Sid = sid };
        Accounts.Add(newEntry);
        return newEntry;
    }

    public void RemoveAccountIfEmpty(string sid)
    {
        var entry = GetAccount(sid);
        if (entry?.IsEmpty == true)
            Accounts.Remove(entry);
    }

    /// <summary>
    /// Records a SID-to-username association in the central map. No-op if SID or name is empty.
    /// </summary>
    public void UpdateSidName(string sid, string name)
    {
        if (string.IsNullOrEmpty(sid) || string.IsNullOrEmpty(name))
            return;
        SidNames[sid] = name;
    }

    /// <summary>
    /// Creates a shallow-cloned snapshot safe for use on background threads.
    /// All list and dictionary properties are cloned so concurrent modification on the
    /// UI thread does not corrupt the snapshot.
    /// </summary>
    public AppDatabase CreateSnapshot() => new()
    {
        Version = Version,
        Apps = Apps.ToList(),
        AppContainers = AppContainers.ToList(),
        Settings = Settings.Clone(),
        LastPrefsFilePath = LastPrefsFilePath,
        SidNames = new Dictionary<string, string>(SidNames, StringComparer.OrdinalIgnoreCase),
        Accounts = Accounts.Select(a => a.Clone()).ToList(),
        AccountGroupSnapshots = AccountGroupSnapshots?.ToDictionary(
            kvp => kvp.Key, kvp => kvp.Value.ToList(), StringComparer.OrdinalIgnoreCase),
    };
}