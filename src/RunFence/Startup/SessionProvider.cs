using Autofac;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Startup;

public class SessionProvider : ISessionProvider, IDatabaseProvider
{
    private SessionContext? _session;
    private ILifetimeScope? _sessionScope;

    public void SetSession(SessionContext session) => _session = session;
    public void SetSessionScope(ILifetimeScope sessionScope) => _sessionScope = sessionScope;
    public void ClearSessionScope(ILifetimeScope sessionScope)
    {
        if (ReferenceEquals(_sessionScope, sessionScope))
            _sessionScope = null;
    }

    public SessionContext GetSession() => _session ?? throw new InvalidOperationException("Session not initialized");
    public ILifetimeScope GetSessionScope() => _sessionScope ?? throw new InvalidOperationException("Session scope not initialized");
    public AppDatabase GetDatabase() => GetSession().Database;
}
