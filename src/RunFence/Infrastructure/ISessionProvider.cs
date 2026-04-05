using RunFence.Core.Models;

namespace RunFence.Infrastructure;

/// <summary>
/// Provides the current <see cref="SessionContext"/> instance.
/// Allows background services to access the live session without capturing a closure.
/// </summary>
public interface ISessionProvider
{
    SessionContext GetSession();
}