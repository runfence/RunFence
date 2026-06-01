using RunFence.Core.Infrastructure;

namespace RunFence.Infrastructure;

public sealed class SaveFileDialogAdapter : ISaveFileDialogAdapter
{
    private readonly SaveFileDialog _dialog = new();

    public SaveFileDialog Dialog => _dialog;

    public DialogResult ShowDialog(IWin32Window? owner)
    {
        FileDialogHelper.AddInteractiveUserCustomPlaces(_dialog);
        return _dialog.ShowDialog(owner);
    }

    public void Dispose()
        => _dialog.Dispose();
}
