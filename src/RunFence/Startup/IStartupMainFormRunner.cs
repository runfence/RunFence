using Autofac;

namespace RunFence.Startup;

/// <summary>
/// Resolves <c>MainForm</c>, <c>AppLifecycleStarter</c>, and <c>IFolderHandlerService</c>
/// from the session lifetime scope, runs <c>Application.Run(mainForm)</c>, and calls
/// <c>UnregisterAll()</c> after the UI exits.
/// </summary>
public interface IStartupMainFormRunner
{
    /// <summary>
    /// Runs the main form within <paramref name="sessionScope"/> and blocks until the UI exits.
    /// </summary>
    /// <param name="sessionScope">The session lifetime scope created by <see cref="IStartupSessionScopeFactory"/>.</param>
    void Run(ILifetimeScope sessionScope);
}
