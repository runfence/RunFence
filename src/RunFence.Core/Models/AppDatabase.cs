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
    /// Durable JobKeeper routing identities. Keys are target SID plus restricted integrity mode.
    /// Reconnect is valid only after the named job is reopened and verified.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, JobKeeperInstanceIdentity>? JobKeeperInstances { get; set; }

    /// <summary>
    /// Durable list of SIDs whose unrestricted tracking jobs are known to exist.
    /// Entries are stored case-insensitively and de-duplicated by callers that mutate the list.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? TrackingJobSids { get; set; }

    /// <summary>
    /// Snapshot of each known SID's local group memberships at the last reconciliation check.
    /// Used to detect group membership changes between sessions and re-apply traverse ACEs
    /// when a user is added to or removed from a group. Keys are SIDs (case-insensitive).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, List<string>>? AccountGroupSnapshots { get; set; }

    /// <summary>
    /// When true, the built-in SYSTEM account (S-1-5-18) appears as the first item
    /// in the RunAs credential dropdown.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ShowSystemInRunAs { get; set; }

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
    /// <remarks>
    /// Convention: background consumers must treat <see cref="AppEntry"/> objects in the snapshot
    /// as read-only. Deep-cloning every AppEntry would be prohibitively expensive and is unnecessary
    /// because all existing background consumers only read entry fields — they never mutate them.
    /// </remarks>
    public AppDatabase CreateSnapshot() => new()
    {
        Version = Version,
        Apps = Apps.Select(a => a.Clone()).ToList(),
        AppContainers = AppContainers.Select(c => c.Clone()).ToList(),
        Settings = Settings.Clone(),
        LastPrefsFilePath = LastPrefsFilePath,
        SidNames = new Dictionary<string, string>(SidNames, StringComparer.OrdinalIgnoreCase),
        JobKeeperInstances = JobKeeperInstances?.ToDictionary(
            kvp => kvp.Key, kvp => kvp.Value with { }, StringComparer.OrdinalIgnoreCase),
        TrackingJobSids = TrackingJobSids?.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        Accounts = Accounts.Select(a => a.Clone()).ToList(),
        AccountGroupSnapshots = AccountGroupSnapshots?.ToDictionary(
            kvp => kvp.Key, kvp => kvp.Value.ToList(), StringComparer.OrdinalIgnoreCase),
        ShowSystemInRunAs = ShowSystemInRunAs,
    };

    /// <summary>
    /// Replaces this instance with the provided snapshot contents.
    /// </summary>
    public void ReplaceWithSnapshot(AppDatabase snapshot)
    {
        Version = snapshot.Version;
        Apps = snapshot.Apps.Select(a => a.Clone()).ToList();
        Accounts = snapshot.Accounts.Select(a => a.Clone()).ToList();
        AppContainers = snapshot.AppContainers.Select(c => c.Clone()).ToList();
        Settings = snapshot.Settings.Clone();
        LastPrefsFilePath = snapshot.LastPrefsFilePath;
        SidNames = new Dictionary<string, string>(snapshot.SidNames, StringComparer.OrdinalIgnoreCase);
        JobKeeperInstances = snapshot.JobKeeperInstances?.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value with { },
            StringComparer.OrdinalIgnoreCase);
        TrackingJobSids = snapshot.TrackingJobSids?.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        AccountGroupSnapshots = snapshot.AccountGroupSnapshots?.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToList(),
            StringComparer.OrdinalIgnoreCase);
        ShowSystemInRunAs = snapshot.ShowSystemInRunAs;
    }
}
