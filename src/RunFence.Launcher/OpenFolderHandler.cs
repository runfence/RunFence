using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;
using RunFence.Core;
using RunFence.Core.Ipc;

namespace RunFence.Launcher;

/// <summary>
/// Handles the <c>--open-folder</c> mode invoked by the custom folder shell handler
/// registered in HKU\&lt;sid&gt;\Software\Classes\Directory\shell\open.
/// </summary>
public static class OpenFolderHandler
{
    public static int Handle(string folderPath)
    {
        // 1. Admin check: if this process is admin-capable, unregister own handler
        //    (account was elevated since registration) and launch explorer directly.
        if (IsCurrentProcessAdmin())
        {
            UnregisterOwnHandler();
            LaunchExplorerDirect(folderPath);
            return 0;
        }

        // 2. Own-explorer check: if explorer.exe is already running under our SID, this account
        //    is now the interactive user. Unregister our handler (no longer needed) and open directly.
        if (IsOwnExplorerRunning())
        {
            UnregisterOwnHandler();
            LaunchExplorerDirect(folderPath);
            return 0;
        }

        // 3. IPC path: RunFence validates the path and opens it from its elevated process.
        var message = new IpcMessage
        {
            Command = IpcCommands.OpenFolder,
            Arguments = folderPath
        };
        var response = LauncherIpcHelper.SendWithAutoStart(message);
        if (response == null)
            return 1;
        if (!response.Success)
        {
            LauncherIpcHelper.ShowError($"{response.ErrorMessage ?? "Could not open folder."}\n\n{folderPath}");
            return 1;
        }

        return 0;
    }

    // ── Admin check ───────────────────────────────────────────────────────────

    private static bool IsCurrentProcessAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    // ── Own-explorer detection ────────────────────────────────────────────────

    private static bool IsOwnExplorerRunning()
    {
        try
        {
            var ownSid = WindowsIdentity.GetCurrent().User?.Value;
            if (string.IsNullOrEmpty(ownSid))
                return false;

            var procs = Process.GetProcessesByName("explorer");
            try
            {
                foreach (var proc in procs)
                {
                    try
                    {
                        var sid = NativeTokenHelper.TryGetProcessOwnerSid((uint)proc.Id);
                        if (string.Equals(sid?.Value, ownSid, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    catch
                    {
                    }
                }

                return false;
            }
            finally
            {
                foreach (var proc in procs)
                    proc.Dispose();
            }
        }
        catch
        {
            return false;
        }
    }

    // ── Direct explorer launch ────────────────────────────────────────────────

    private static void LaunchExplorerDirect(string folderPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folderPath}\"",
                UseShellExecute = false
            });
        }
        catch
        {
        }
    }

    // ── Self-unregister handler when admin ────────────────────────────────────

    private static void UnregisterOwnHandler()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\shell\open",
                throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\shell\explore",
                throwOnMissingSubKey: false);
            try
            {
                using var shellKey = Registry.CurrentUser.OpenSubKey(
                    @"Software\Classes\Directory\shell", writable: true);
                shellKey?.DeleteValue("", throwOnMissingValue: false);
            }
            catch
            {
            }

            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Folder\shell\open",
                throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(
                @"Software\Classes\CLSID\{9BA05972-F6A8-11CF-A442-00A0C90A8F39}",
                throwOnMissingSubKey: false);
            OpenFolderNative.SHChangeNotify(OpenFolderNative.SHCNE_ASSOCCHANGED, OpenFolderNative.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
        }
    }
}