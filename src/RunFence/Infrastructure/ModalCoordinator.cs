namespace RunFence.Infrastructure;

/// <summary>
/// DI-registered implementation of <see cref="IModalCoordinator"/>.
/// Combines <see cref="IModalTracker"/> and <see cref="ISecureDesktopRunner"/> so callers
/// do not need to depend on both separately. Replaces the static accessors on DataPanel.
/// </summary>
public class ModalCoordinator(IModalTracker modalTracker, ISecureDesktopRunner secureDesktopRunner)
    : IModalCoordinator
{
    public void BeginModal() => modalTracker.BeginModal();

    public void EndModal() => modalTracker.EndModal();

    public DialogResult ShowModal(Form dialog, IWin32Window? owner)
    {
        BeginModal();
        try
        {
            return dialog.ShowDialog(owner);
        }
        finally
        {
            EndModal();
        }
    }

    public void RunOnSecureDesktop(Action action)
    {
        BeginModal();
        try
        {
            secureDesktopRunner.Run(action);
        }
        finally
        {
            EndModal();
        }
    }
}
