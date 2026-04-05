using System.Drawing.Imaging;
using RunFence.Infrastructure;

namespace RunFence.Acl.UI;

/// <summary>
/// Provides cached 16×16 shell icons for file-system paths displayed in ACL Manager grids.
/// Icons are cached by (isDirectory, isReparsePoint) since all folders share one icon and
/// all reparse-point folders share another. File icons are similarly generic.
/// </summary>
public static class AclPathIconProvider
{
    private static readonly Dictionary<(bool isDir, bool isReparse), Bitmap> Cache = new();

    /// <summary>
    /// Returns a 16×16 icon for the path. Detects directory/file type and reparse-point status
    /// from the filesystem; falls back gracefully for missing paths or P/Invoke failures.
    /// </summary>
    public static Bitmap GetIcon(string path)
    {
        bool isDirectory;
        bool isReparsePoint = false;
        try
        {
            var attrs = File.GetAttributes(path);
            isDirectory = (attrs & FileAttributes.Directory) != 0;
            isReparsePoint = (attrs & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            // Path missing or access denied — guess type by extension, no reparse overlay
            isDirectory = string.IsNullOrEmpty(Path.GetExtension(path));
        }

        var key = (isDirectory, isReparsePoint);
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var bmp = FetchIcon(isDirectory, isReparsePoint) ?? CreateTransparentBitmap();
        Cache[key] = bmp;
        return bmp;
    }

    private static Bitmap? FetchIcon(bool isDirectory, bool isReparsePoint)
    {
        uint flags = ShellIconHelper.SHGFI_ICON | ShellIconHelper.SHGFI_SMALLICON | ShellIconHelper.SHGFI_USEFILEATTRIBUTES;
        if (isReparsePoint)
            flags |= ShellIconHelper.SHGFI_LINKOVERLAY;

        string fakePath = isDirectory ? "folder" : "file.txt";
        uint attrs = isDirectory ? ShellIconHelper.FILE_ATTRIBUTE_DIRECTORY : ShellIconHelper.FILE_ATTRIBUTE_NORMAL;

        var hIcon = ShellIconHelper.GetFileIconHandle(fakePath, attrs, flags);
        if (hIcon == IntPtr.Zero)
            return null;

        try
        {
            using var icon = Icon.FromHandle(hIcon);
            var bmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.DrawImage(icon.ToBitmap(), 0, 0, 16, 16);
            return bmp;
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }

    private static Bitmap CreateTransparentBitmap() => new(16, 16, PixelFormat.Format32bppArgb);
}