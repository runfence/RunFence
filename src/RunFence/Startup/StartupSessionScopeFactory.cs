using Autofac;
using RunFence.Core.Models;

namespace RunFence.Startup;

/// <summary>
/// Production implementation of <see cref="IStartupSessionScopeFactory"/>.
/// Delegates to <see cref="ContainerRegistrationBuilder.BeginSessionScope"/>.
/// Receives the foundation <see cref="ILifetimeScope"/> (the root container) so that
/// the session scope is always a direct child and sees all foundation-scope singletons.
/// Autofac automatically registers <see cref="ILifetimeScope"/> in every scope.
/// </summary>
public class StartupSessionScopeFactory(ILifetimeScope foundationScope) : IStartupSessionScopeFactory
{
    public ILifetimeScope BeginSessionScope(SessionContext session, StartupOptions options) =>
        ContainerRegistrationBuilder.BeginSessionScope(foundationScope, session, options);
}
