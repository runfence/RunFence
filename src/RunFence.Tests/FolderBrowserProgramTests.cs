using RunFence.FolderBrowser;
using Xunit;

namespace RunFence.Tests;

public class FolderBrowserProgramTests
{
    [Fact]
    public void ShowDialogAndLaunch_CallsAddInteractiveUserCustomPlacesBeforeShowDialog()
    {
        using var owner = new Form();
        using var adapter = new RecordingOpenFileDialogAdapter();

        RunFence.FolderBrowser.Program.ShowDialogAndLaunch(owner, Environment.CurrentDirectory, adapter);

        Assert.Equal(["add", "show"], adapter.Calls);
    }

    private sealed class RecordingOpenFileDialogAdapter : RunFence.FolderBrowser.Program.IOpenFileDialogAdapter
    {
        public OpenFileDialog Dialog { get; } = new();
        public List<string> Calls { get; } = [];

        public void AddInteractiveUserCustomPlaces() => Calls.Add("add");
        public DialogResult ShowDialog(IWin32Window? owner)
        {
            Calls.Add("show");
            return DialogResult.Cancel;
        }

        public void Dispose() => Dialog.Dispose();
    }
}
