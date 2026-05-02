using Autofac;
using RunFence.Core.Models;

namespace RunFence.Startup;

/// <summary>
/// Creates the session lifetime scope from the foundation container.
/// Wraps <see cref="ContainerRegistrationBuilder.BeginSessionScope"/> for testability.
/// </summary>
public interface IStartupSessionScopeFactory
{
    ILifetimeScope BeginSessionScope(SessionContext session, StartupOptions options);
}
