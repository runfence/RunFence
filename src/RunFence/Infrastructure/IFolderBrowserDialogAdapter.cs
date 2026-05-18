namespace RunFence.Infrastructure;

public interface IFolderBrowserDialogAdapter : IDisposable
{
    FolderBrowserDialog Dialog { get; }
    DialogResult ShowDialog(IWin32Window? owner);
}
