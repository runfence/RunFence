namespace RunFence.Infrastructure;

public interface IOpenFileDialogAdapter : IDisposable
{
    OpenFileDialog Dialog { get; }
    DialogResult ShowDialog(IWin32Window? owner);
}
