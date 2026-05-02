using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Firewall;

public class FirewallSettingsService(ISessionProvider sessionProvider) : IFirewallSettingsService
{
    public (AppDatabase Database, string Username) GetDatabaseAndUsername(string sid)
    {
        var database = sessionProvider.GetSession().Database;
        var username = database.SidNames.GetValueOrDefault(sid) ?? sid;
        return (database, username);
    }
}
