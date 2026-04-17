using RunFence.Core.Models;

namespace RunFence.Tests;

/// <summary>
/// Shared helpers for firewall-related unit tests.
/// </summary>
internal static class FirewallTestHelpers
{
    private const string DefaultSid = "S-1-5-21-1000-1000-1000-1001";
    private const string DefaultUsername = "alice";
    private const string DefaultDomain = "example.com";

    /// <summary>
    /// Returns an empty case-insensitive set of changed domain names, used when a cache
    /// refresh decision should not be influenced by any domain resolution change.
    /// </summary>
    public static IReadOnlySet<string> EmptyChangedDomains() =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a resolved-domains dictionary mapping <c>"example.com"</c> to the given address.
    /// </summary>
    public static Dictionary<string, List<string>> Resolved(string address) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultDomain] = [address]
        };

    /// <summary>
    /// Creates a <see cref="FirewallAccountSettings"/> with internet blocked and optional allowlist entries.
    /// </summary>
    public static FirewallAccountSettings BlockInternet(
        bool addDomainEntry = false,
        bool addIpEntry = false,
        bool allowLocalhost = true,
        string domain = DefaultDomain)
    {
        var settings = new FirewallAccountSettings
        {
            AllowInternet = false,
            AllowLocalhost = allowLocalhost,
            AllowLan = true
        };
        if (addDomainEntry)
            settings.Allowlist.Add(new FirewallAllowlistEntry { Value = domain, IsDomain = true });
        if (addIpEntry)
            settings.Allowlist.Add(new FirewallAllowlistEntry { Value = "10.0.0.1", IsDomain = false });
        return settings;
    }

    /// <summary>
    /// Creates a <see cref="FirewallAccountSettings"/> with internet blocked and multiple domain allowlist entries.
    /// </summary>
    public static FirewallAccountSettings BlockInternetWithDomains(params string[] domains)
    {
        var settings = BlockInternet();
        foreach (var domain in domains)
            settings.Allowlist.Add(new FirewallAllowlistEntry { Value = domain, IsDomain = true });
        return settings;
    }

    /// <summary>
    /// Creates an <see cref="AppDatabase"/> with a single account using the standard test SID (<c>S-1-5-21-1000-1000-1000-1001</c>) and username (<c>alice</c>).
    /// </summary>
    public static AppDatabase BuildDatabase(FirewallAccountSettings settings) =>
        BuildDatabase(settings, DefaultSid, DefaultUsername);

    /// <summary>
    /// Creates an <see cref="AppDatabase"/> with the standard test account (using <paramref name="firstSettings"/>),
    /// then adds a second account identified by <paramref name="sid"/> and <paramref name="username"/> with <paramref name="secondSettings"/>.
    /// </summary>
    public static AppDatabase BuildDatabase(
        FirewallAccountSettings firstSettings,
        string sid,
        string username,
        FirewallAccountSettings secondSettings)
    {
        var database = BuildDatabase(firstSettings);
        database.SidNames[sid] = username;
        database.GetOrCreateAccount(sid).Firewall = secondSettings;
        return database;
    }

    private static AppDatabase BuildDatabase(
        FirewallAccountSettings settings,
        string sid,
        string username)
    {
        var database = new AppDatabase
        {
            SidNames =
            {
                [sid] = username
            }
        };
        database.GetOrCreateAccount(sid).Firewall = settings;
        return database;
    }
}
