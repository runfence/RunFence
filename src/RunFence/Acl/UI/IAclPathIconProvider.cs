namespace RunFence.Acl.UI;

public interface IAclPathIconProvider
{
    /// <summary>
    /// Returns a 16×16 icon for the path. Detects directory/file type and reparse-point status
    /// from the filesystem; falls back gracefully for missing paths or P/Invoke failures.
    /// </summary>
    Bitmap GetIcon(string path);
}
