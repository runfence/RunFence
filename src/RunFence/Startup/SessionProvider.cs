using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Startup;

public class SessionProvider : ISessionProvider, IDatabaseProvider
{
    private SessionContext? _session;

    public void SetSession(SessionContext session) => _session = session;
    public SessionContext GetSession() => _session ?? throw new InvalidOperationException("Session not initialized");
    public AppDatabase GetDatabase() => GetSession().Database;
}