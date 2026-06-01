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
public class OpenFolderHandler(
    ILauncherIpcCommandSender commandSender,
    ILauncherProcessStarter processStarter,
    ILauncherUserNotifier notifier)
{
    private const string DefaultClassesRootPath = @"Software\Classes";

    public int Handle(string folderPath)
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
        var response = commandSender.SendWithAutoStart(message);
        if (response == null)
            return 1;
        if (!response.Success)
        {
            notifier.ShowError($"{response.ErrorMessage ?? "Could not open folder."}\n\n{folderPath}");
            return 1;
        }

        return 0;
    }

    public int Unregister()
    {
        UnregisterOwnHandler();
        return 0;
    }

    protected virtual bool IsCurrentProcessAdmin()
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

    protected virtual bool IsOwnExplorerRunning()
    {
        try
        {
            var ownSid = WindowsIdentity.GetCurrent().User?.Value;
            if (string.IsNullOrEmpty(ownSid))
                return false;

            foreach (var process in GetExplorerProcesses())
            {
                if (process.SessionId != GetCurrentSessionId())
                    continue;

                try
                {
                    var sid = TryGetProcessOwnerSid(process.ProcessId);
                    if (string.Equals(sid?.Value, ownSid, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch
                {
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    protected virtual int GetCurrentSessionId() => Process.GetCurrentProcess().SessionId;

    protected virtual IEnumerable<ExplorerProcessInfo> GetExplorerProcesses()
    {
        var procs = NativeTokenHelper.GetProcessesByNameInCurrentSession("explorer");
        try
        {
            foreach (var proc in procs)
                yield return new ExplorerProcessInfo((uint)proc.Id, proc.SessionId);
        }
        finally
        {
            foreach (var proc in procs)
                proc.Dispose();
        }
    }

    protected virtual SecurityIdentifier? TryGetProcessOwnerSid(uint processId)
        => NativeTokenHelper.TryGetProcessOwnerSid(processId);

    protected virtual void LaunchExplorerDirect(string folderPath)
    {
        var folderArguments = CommandLineHelper.MaterializeProcessArguments([folderPath]) ?? string.Empty;
        processStarter.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = folderArguments,
            UseShellExecute = false
        });
    }

    protected virtual void UnregisterOwnHandler()
    {
        try
        {
            using var root = OpenRegistryRoot();
            CreateOwnedRegistryCleaner(root).UnregisterOwnedFolderHandler();
            NotifyShellAssociationsChanged();
        }
        catch
        {
        }
    }

    protected virtual string GetClassesRootPath() => DefaultClassesRootPath;

    protected virtual void NotifyShellAssociationsChanged()
    {
        OpenFolderNative.SHChangeNotify(
            OpenFolderNative.SHCNE_ASSOCCHANGED,
            OpenFolderNative.SHCNF_IDLIST,
            IntPtr.Zero,
            IntPtr.Zero);
    }

    protected virtual string GetOwnedLauncherExeName() => PathConstants.LauncherExeName;

    protected virtual string GetOwnedShellServerExeName() => PathConstants.ShellServerExeName;

    protected virtual IRegistryKey OpenRegistryRoot()
        => new WindowsRegistryKey(Registry.CurrentUser);

    private FolderHandlerOwnedRegistryCleaner CreateOwnedRegistryCleaner(IRegistryKey root)
    {
        return new FolderHandlerOwnedRegistryCleaner(
            root,
            GetClassesRootPath(),
            GetOwnedLauncherExeName(),
            GetOwnedShellServerExeName());
    }

    protected readonly record struct ExplorerProcessInfo(uint ProcessId, int SessionId);
}
