using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Tests;

/// <summary>
/// Lightweight lambda-based adapters for service interfaces used in unit tests.
/// </summary>
public sealed class LambdaSessionProvider(Func<SessionContext> getSession) : ISessionProvider
{
    public SessionContext GetSession() => getSession();
}

public sealed class InlineUiThreadInvoker(Action<Action> invoke) : IUiThreadInvoker
{
    public void Invoke(Action action) => invoke(action);
    public void BeginInvoke(Action action) => invoke(action);
    public void RunOnUiThread(Action action) => invoke(action);
}