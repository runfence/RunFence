using RunFence.Core.Models;

namespace RunFence.Startup.NonElevatedMocks;

public class NonElevatedMockStore
{
    private readonly MockSidGenerator _sidGenerator;
    private readonly Dictionary<string, string> _users = new(StringComparer.OrdinalIgnoreCase); // SID -> username (fake users)
    private readonly Dictionary<string, (string Name, string? Description)> _groups = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _knownUsernames = new(StringComparer.OrdinalIgnoreCase); // SID -> username (real users seen via AddMemberships)
    private readonly Dictionary<string, HashSet<string>> _memberships = new(StringComparer.OrdinalIgnoreCase); // userSid -> groupSids

    public NonElevatedMockStore() : this(new MockSidGenerator())
    {
    }

    public NonElevatedMockStore(MockSidGenerator sidGenerator)
    {
        ArgumentNullException.ThrowIfNull(sidGenerator);
        _sidGenerator = sidGenerator;
    }

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

    public string CreateUserSid(string username, uint ridBase = 20001)
        => _sidGenerator.DeriveFakeSid(username, ridBase);

    public string CreateGroupSid(string groupName, uint ridBase = 30001)
        => _sidGenerator.DeriveFakeSid(groupName, ridBase);
}
