namespace RunFence.UI;

/// <summary>
/// Abstracts the minimal set of MainForm members needed by <see cref="MainFormTrayHandler"/>.
/// Keeps the tray handler decoupled from the concrete form type.
/// </summary>
public interface IMainFormVisibility
{
    void Show();
    void Hide();
    bool IsDisposed { get; }
    bool IsHandleCreated { get; }
    FormWindowState WindowState { get; set; }
    bool ShowInTaskbar { get; set; }
    string Title { set; }
    IntPtr Handle { get; }
    void BringToFront();
    void BeginInvokeOnUiThread(Action action);
    void InvokeOnUiThread(Action action);
}