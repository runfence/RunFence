namespace RunFence.UI;

/// <summary>
/// Abstracts the minimal set of MainForm members needed by <see cref="MainFormTrayHandler"/>,
/// <see cref="MainFormWindowRequestHandler"/>, and <see cref="MainFormBackgroundAutoLockCoordinator"/>.
/// Keeps form handlers decoupled from the concrete form type.
/// </summary>
public interface IMainFormVisibility
{
    void Show();
    void Hide();
    bool IsDisposed { get; }
    bool IsHandleCreated { get; }
    bool Visible { get; }
    FormWindowState WindowState { get; set; }
    bool ShowInTaskbar { get; set; }
    string Title { set; }
    IntPtr Handle { get; }
    bool IsModalActive { get; }
    bool HasOtherWindowsOpen { get; }
    void BringToFront();
    void BeginInvokeOnUiThread(Action action);
    void InvokeOnUiThread(Action action);
}