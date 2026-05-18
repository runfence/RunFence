namespace RunFence.Infrastructure;

public sealed class FolderBrowserDialogAdapter : IFolderBrowserDialogAdapter
{
    private readonly FolderBrowserDialog _dialog = new();

    public FolderBrowserDialog Dialog => _dialog;

    public DialogResult ShowDialog(IWin32Window? owner)
        => _dialog.ShowDialog(owner);

    public void Dispose()
        => _dialog.Dispose();
}
