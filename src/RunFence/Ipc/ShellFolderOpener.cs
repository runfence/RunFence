using System.Runtime.InteropServices;
using RunFence.Infrastructure;

namespace RunFence.Ipc;

/// <summary>
/// Opens a folder in Explorer using SHParseDisplayName + ShellExecuteEx with SEE_MASK_IDLIST.
/// The PIDL (not the raw path string) is passed to ShellExecuteEx, preventing TOCTOU path-swap attacks.
/// The "explore" verb is folder-specific: it fails on files that lack it, providing defense-in-depth
/// alongside the held directory handle from <see cref="IDirectoryValidator"/>.
/// Must be called on an STA (UI) thread.
/// </summary>
public class ShellFolderOpener : IShellFolderOpener
{
    public bool TryOpen(string canonicalPath, out string? errorMessage)
    {
        var hr = ShellNative.SHParseDisplayName(canonicalPath, IntPtr.Zero, out var pidl, 0, out _);
        if (hr != 0 || pidl == IntPtr.Zero)
        {
            errorMessage = $"SHParseDisplayName failed (hr={hr:X8})";
            return false;
        }

        try
        {
            var sei = new ShellNative.ShellExecuteExInfo
            {
                cbSize = Marshal.SizeOf<ShellNative.ShellExecuteExInfo>(),
                fMask = ShellNative.SeeMaskIdList | ShellNative.SeeMaskFlagNoUi,
                lpVerb = "explore",
                lpIDList = pidl,
                nShow = ShellNative.SwShownormal,
            };
            if (!ShellNative.ShellExecuteEx(ref sei))
            {
                var err = Marshal.GetLastWin32Error();
                errorMessage = $"ShellExecuteEx failed (err={err})";
                return false;
            }

            errorMessage = null;
            return true;
        }
        finally
        {
            ShellNative.CoTaskMemFree(pidl);
        }
    }
}