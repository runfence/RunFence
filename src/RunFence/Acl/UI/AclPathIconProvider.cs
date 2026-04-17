using System.Drawing.Imaging;
using RunFence.Infrastructure;

namespace RunFence.Acl.UI;

/// <summary>
/// Provides cached 16×16 shell icons for file-system paths displayed in ACL Manager grids.
/// Icons are cached by (isDirectory, isReparsePoint) since all folders share one icon and
/// all reparse-point folders share another. File icons are similarly generic.
/// </summary>
public class AclPathIconProvider : IAclPathIconProvider, IDisposable
{
    private readonly Dictionary<(bool isDir, bool isReparse), Bitmap> _cache = new();

    /// <summary>
    /// Returns a 16×16 icon for the path. Detects directory/file type and reparse-point status
    /// from the filesystem; falls back gracefully for missing paths or P/Invoke failures.
    /// </summary>
    public Bitmap GetIcon(string path)
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
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var bmp = FetchIcon(isDirectory, isReparsePoint) ?? CreateTransparentBitmap();
        _cache[key] = bmp;
        return bmp;
    }

    public void Dispose()
    {
        foreach (var bmp in _cache.Values)
            bmp.Dispose();
        _cache.Clear();
    }

    private static Bitmap? FetchIcon(bool isDirectory, bool isReparsePoint)
    {
        uint flags = ShellNative.SHGFI_ICON | ShellNative.SHGFI_SMALLICON | ShellNative.SHGFI_USEFILEATTRIBUTES;
        if (isReparsePoint)
            flags |= ShellNative.SHGFI_LINKOVERLAY;

        string fakePath = isDirectory ? "folder" : "file.txt";
        uint attrs = isDirectory ? FileSecurityNative.FILE_ATTRIBUTE_DIRECTORY : FileSecurityNative.FILE_ATTRIBUTE_NORMAL;

        var hIcon = ShellNative.GetFileIconHandle(fakePath, attrs, flags);
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
            WindowNative.DestroyIcon(hIcon);
        }
    }

    private static Bitmap CreateTransparentBitmap() => new(16, 16, PixelFormat.Format32bppArgb);
}
