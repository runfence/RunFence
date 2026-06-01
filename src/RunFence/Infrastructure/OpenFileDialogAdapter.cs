using RunFence.Core.Infrastructure;

namespace RunFence.Infrastructure;

public sealed class OpenFileDialogAdapter : IOpenFileDialogAdapter
{
    private readonly OpenFileDialog _dialog = new();

    public OpenFileDialog Dialog => _dialog;

    public DialogResult ShowDialog(IWin32Window? owner)
    {
        FileDialogHelper.AddInteractiveUserCustomPlaces(_dialog);
        return _dialog.ShowDialog(owner);
    }

    public void Dispose()
        => _dialog.Dispose();
}
