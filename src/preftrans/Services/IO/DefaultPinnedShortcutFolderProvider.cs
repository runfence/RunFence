namespace PrefTrans.Services.IO;

public class DefaultPinnedShortcutFolderProvider : IPinnedShortcutFolderProvider
{
    private const string TaskbarPinnedFolderRelativePath =
        @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar";

    public string GetPinnedShortcutFolder()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            TaskbarPinnedFolderRelativePath);
    }
}
