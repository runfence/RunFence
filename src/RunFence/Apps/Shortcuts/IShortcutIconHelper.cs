namespace RunFence.Apps.Shortcuts;

/// <summary>
/// Extracts and resizes application icons from executable files.
/// </summary>
public interface IShortcutIconHelper
{
    /// <summary>
    /// Extracts the associated icon from <paramref name="exePath"/> and resizes it to
    /// <paramref name="size"/> × <paramref name="size"/> pixels. Returns null if the file does
    /// not exist, if icon extraction returns null, or if an exception occurs.
    /// </summary>
    Image? ExtractIcon(string exePath, int size = 16);
}
