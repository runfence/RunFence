namespace RunFence.Apps.Shortcuts;

public sealed class ExecutableIconCountReader(IShortcutIconNativeApi shortcutIconNativeApi) : IExecutableIconCountReader
{
    public bool TryGetIconCount(string path, out int iconCount)
    {
        try
        {
            iconCount = shortcutIconNativeApi.ExtractIconEx(path, -1, null, null, 0);
            return true;
        }
        catch (Exception)
        {
            iconCount = 0;
            return false;
        }
    }
}
