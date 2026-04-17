namespace RunFence.Infrastructure;

/// <summary>
/// Coordinates modal dialog tracking and secure desktop execution.
/// Wraps <see cref="IModalTracker"/> and <see cref="ISecureDesktopRunner"/>
/// into a single service so callers do not depend on both separately.
/// </summary>
public interface IModalCoordinator
{
    /// <summary>
    /// Signals that a modal dialog is about to open. Must be paired with <see cref="EndModal"/> in a finally block.
    /// </summary>
    void BeginModal();

    /// <summary>Signals that a modal dialog was closed.</summary>
    void EndModal();

    /// <summary>
    /// Shows a modal dialog with an explicit owner, wrapped with <see cref="BeginModal"/>/<see cref="EndModal"/> tracking.
    /// </summary>
    DialogResult ShowModal(Form dialog, IWin32Window? owner);

    /// <summary>
    /// Runs <paramref name="action"/> on the secure desktop, wrapped with <see cref="BeginModal"/>/<see cref="EndModal"/> tracking.
    /// Use for dialogs that accept sensitive input (passwords, credentials).
    /// </summary>
    void RunOnSecureDesktop(Action action);
}
