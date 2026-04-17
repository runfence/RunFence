using System.Text.Json.Serialization;

namespace RunFence.Core.Models;

public class FirewallAccountSettings
{
    public bool AllowInternet { get; set; } = true;
    public bool AllowLocalhost { get; set; } = true;
    public bool AllowLan { get; set; } = true;
    public List<FirewallAllowlistEntry> Allowlist { get; set; } = new();
    public List<string> LocalhostPortExemptions { get; set; } = ["53", "49152-65535"];
    public bool FilterEphemeralLoopback { get; set; } = true;

    [JsonIgnore]
    public bool IsDefault => AllowInternet && AllowLocalhost && AllowLan && Allowlist.Count == 0
        && FilterEphemeralLoopback
        && LocalhostPortExemptions.Count == 2 && LocalhostPortExemptions[0] == "53"
        && LocalhostPortExemptions[1] == "49152-65535";

    public FirewallAccountSettings Clone() => new()
    {
        AllowInternet = AllowInternet,
        AllowLocalhost = AllowLocalhost,
        AllowLan = AllowLan,
        Allowlist = Allowlist.Select(entry => new FirewallAllowlistEntry
        {
            Value = entry.Value,
            IsDomain = entry.IsDomain
        }).ToList(),
        LocalhostPortExemptions = LocalhostPortExemptions.ToList(),
        FilterEphemeralLoopback = FilterEphemeralLoopback
    };

    /// <summary>
    /// Stores the settings in the database (via AccountEntry), or removes the entry if settings are default.
    /// </summary>
    public static void UpdateOrRemove(AppDatabase database, string sid, FirewallAccountSettings settings)
    {
        database.GetOrCreateAccount(sid).Firewall = settings;
        database.RemoveAccountIfEmpty(sid);
    }
}
