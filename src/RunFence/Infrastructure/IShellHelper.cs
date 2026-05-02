namespace RunFence.Infrastructure;

public interface IShellHelper
{
    void ShowProperties(string path, IWin32Window? owner = null);
    void OpenInExplorer(string path);
    void OpenDefaultAppsSettings();
    void OpenUrlAsInteractiveUser(string url);
}
