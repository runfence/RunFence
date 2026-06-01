namespace RunFence.Infrastructure;

public interface ISaveFileDialogAdapter : IDisposable
{
    SaveFileDialog Dialog { get; }
    DialogResult ShowDialog(IWin32Window? owner);
}
