using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using RunFence.Core.Models;

namespace RunFence.Startup.NonElevatedMocks;

public class NonElevatedMockStore
{
    private readonly Dictionary<string, string> _users = new(StringComparer.OrdinalIgnoreCase); // SID → username (fake users)
    private readonly Dictionary<string, (string Name, string? Description)> _groups = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _knownUsernames = new(StringComparer.OrdinalIgnoreCase); // SID → username (real users seen via AddMemberships)
    private readonly Dictionary<string, HashSet<string>> _memberships = new(StringComparer.OrdinalIgnoreCase); // userSid → groupSids

    public void AddUser(string sid, string username) => _users[sid] = username;
    public void RemoveUser(string sid) { _users.Remove(sid); _memberships.Remove(sid); }
    public void RenameUser(string sid, string newName) { if (_users.ContainsKey(sid)) _users[sid] = newName; }
    public bool IsFakeUser(string sid) => _users.ContainsKey(sid);
    public bool IsFakeUsername(string username) => _users.Values.Any(u => string.Equals(u, username, StringComparison.OrdinalIgnoreCase));

    public void AddGroup(string sid, string name, string? description) => _groups[sid] = (name, description);
    public void RemoveGroup(string sid) { _groups.Remove(sid); foreach (var set in _memberships.Values) set.Remove(sid); }
    public bool IsFakeGroup(string sid) => _groups.ContainsKey(sid);
    public string? GetGroupDescription(string sid) => _groups.TryGetValue(sid, out var g) ? g.Description : null;
    public void UpdateGroupDescription(string sid, string description)
    {
        if (_groups.TryGetValue(sid, out var g))
            _groups[sid] = (g.Name, description);
    }

    public List<LocalUserAccount> GetAllFakeUsers()
        => _users.Select(kv => new LocalUserAccount(kv.Value, kv.Key)).ToList();

    public List<LocalUserAccount> GetAllFakeGroups()
        => _groups.Select(kv => new LocalUserAccount(kv.Value.Name, kv.Key)).ToList();

    public LocalUserAccount? GetFakeGroup(string sid)
        => _groups.TryGetValue(sid, out var g) ? new LocalUserAccount(g.Name, sid) : null;

    public void AddMemberships(string userSid, string username, IEnumerable<string> groupSids)
    {
        if (!_users.ContainsKey(userSid))
            _knownUsernames[userSid] = username;
        if (!_memberships.TryGetValue(userSid, out var set))
            _memberships[userSid] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in groupSids)
            set.Add(g);
    }

    public void RemoveMemberships(string userSid, IEnumerable<string> groupSids)
    {
        if (_memberships.TryGetValue(userSid, out var set))
            foreach (var g in groupSids)
                set.Remove(g);
    }

    public List<LocalUserAccount> GetMembersOfGroup(string groupSid)
        => _memberships
            .Where(kv => kv.Value.Contains(groupSid))
            .Select(kv => new LocalUserAccount(
                _users.GetValueOrDefault(kv.Key) ?? _knownUsernames.GetValueOrDefault(kv.Key, kv.Key),
                kv.Key))
            .ToList();

    public IEnumerable<string> GetStoredGroupSidsForUser(string userSid)
        => _memberships.TryGetValue(userSid, out var sids) ? sids : [];

    private SecurityIdentifier? _machineSid;

    private SecurityIdentifier? GetMachineSid() => _machineSid ??= WindowsIdentity.GetCurrent().User?.AccountDomainSid;

    // RID ranges: accounts 20001–30000, groups 30001–40000.
    // Machine SID prefix used so SIDs look like real local accounts.
    // Falls back to hash-derived sub-authorities when AccountDomainSid unavailable (domain-account edge case).
    public string DeriveFakeSid(string name, uint ridBase)
    {
        var machineSid = GetMachineSid();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(name.ToUpperInvariant()));
        var rid = ridBase + BitConverter.ToUInt32(hash, 0) % 10000u;
        return machineSid != null
            ? $"{machineSid}-{rid}"
            : $"S-1-5-21-{BitConverter.ToUInt32(hash, 4)}-{BitConverter.ToUInt32(hash, 8)}-{BitConverter.ToUInt32(hash, 12)}-{rid}";
    }
}
