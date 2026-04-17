using Microsoft.Win32;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;

namespace RunFence.Startup;

public class FindingLocationHelper(ILaunchFacade launchFacade, ShellHelper shellHelper)
{
    public void OpenLocation(StartupSecurityFinding finding)
    {
        var target = finding.NavigationTarget;
        if (string.IsNullOrEmpty(target))
            return;

        // MMC snap-ins (Task Scheduler, Local Security Policy)
        // Match bare snap-in names only (no path separators) to avoid catching file paths like C:\...\foo.msc
        if (target.EndsWith(".msc", StringComparison.OrdinalIgnoreCase) &&
            !target.Contains('\\') && !target.Contains('/'))
        {
            LaunchBestEffort("mmc.exe", target);
            return;
        }

        // Disk root Properties dialog
        const string diskPropsPrefix = "diskprops:";
        if (target.StartsWith(diskPropsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var drivePath = target[diskPropsPrefix.Length..];
            OpenDriveProperties(drivePath);
            return;
        }

        // Registry paths (HKEY_...)
        if (target.StartsWith("HKEY_", StringComparison.OrdinalIgnoreCase))
        {
            OpenRegistryKey(target);
            return;
        }

        // File/folder paths
        if (Directory.Exists(target))
        {
            LaunchBestEffort("explorer.exe", $"\"{target}\"");
        }
        else if (File.Exists(target))
        {
            LaunchBestEffort("explorer.exe", $"/select,\"{target}\"");
        }
        else
        {
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                LaunchBestEffort("explorer.exe", $"\"{parent}\"");
        }
    }

    private void OpenDriveProperties(string drivePath)
    {
        try
        {
            shellHelper.ShowProperties(drivePath);
        }
        catch
        {
            /* best effort */
        }
    }

    private void OpenRegistryKey(string fullPath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit", true);
            key?.SetValue("LastKey", fullPath);
        }
        catch
        {
            /* best effort */
        }

        LaunchBestEffort("regedit.exe");
    }

    private void LaunchBestEffort(string fileName, string? arguments = null)
    {
        try
        {
            launchFacade.LaunchFile(fileName, AccountLaunchIdentity.CurrentAccountElevated, arguments)?.Dispose();
        }
        catch
        {
            /* best effort */
        }
    }
}
