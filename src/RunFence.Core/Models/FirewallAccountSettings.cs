using System.Text.Json.Serialization;

namespace RunFence.Core.Models;

public class FirewallAccountSettings
{
    public bool AllowInternet { get; set; } = true;
    public bool AllowLocalhost { get; set; } = true;
    public bool AllowLan { get; set; } = true;
    public List<FirewallAllowlistEntry> Allowlist { get; set; } = new();

    [JsonIgnore]
    public bool IsDefault => AllowInternet && AllowLocalhost && AllowLan && Allowlist.Count == 0;

    public FirewallAccountSettings Clone() => new()
    {
        AllowInternet = AllowInternet,
        AllowLocalhost = AllowLocalhost,
        AllowLan = AllowLan,
        Allowlist = Allowlist.ToList()
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