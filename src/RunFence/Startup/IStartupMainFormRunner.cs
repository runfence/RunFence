using Autofac;
using RunFence.Core;

namespace RunFence.Startup;

/// <summary>
/// Resolves <c>MainForm</c>, <c>AppLifecycleStarter</c>, and <c>IFolderHandlerService</c>
/// from the session lifetime scope, wires <c>PinDerivedKeyReplaced</c>, runs
/// <c>Application.Run(mainForm)</c>, and calls <c>UnregisterAll()</c> after the UI exits.
/// The caller is responsible for supplying the <paramref name="pinDerivedKeyReplaced"/> callback
/// so the orchestrator can track the current key for final disposal.
/// </summary>
public interface IStartupMainFormRunner
{
    /// <summary>
    /// Runs the main form within <paramref name="sessionScope"/> and blocks until the UI exits.
    /// </summary>
    /// <param name="sessionScope">The session lifetime scope created by <see cref="IStartupSessionScopeFactory"/>.</param>
    /// <param name="pinDerivedKeyReplaced">
    /// Callback invoked whenever the main form replaces the PIN-derived key.
    /// Receives (oldBuffer, newBuffer); the caller should dispose <c>oldBuffer</c> and
    /// update its key reference to <c>newBuffer</c>.
    /// </param>
    void Run(ILifetimeScope sessionScope, Action<ProtectedBuffer, ProtectedBuffer> pinDerivedKeyReplaced);
}
