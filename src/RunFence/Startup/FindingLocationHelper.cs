using System.Diagnostics;
using Microsoft.Win32;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Startup;

public static class FindingLocationHelper
{
    public static void OpenLocation(StartupSecurityFinding finding)
    {
        var target = finding.NavigationTarget;
        if (string.IsNullOrEmpty(target))
            return;

        // MMC snap-ins (Task Scheduler, Local Security Policy)
        // Match bare snap-in names only (no path separators) to avoid catching file paths like C:\...\foo.msc
        if (target.EndsWith(".msc", StringComparison.OrdinalIgnoreCase) &&
            !target.Contains('\\') && !target.Contains('/'))
        {
            LaunchMmcSnapIn(target);
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
            LaunchViaExplorer($"\"{target}\"");
        }
        else if (File.Exists(target))
        {
            LaunchViaExplorer($"/select,\"{target}\"");
        }
        else
        {
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                LaunchViaExplorer($"\"{parent}\"");
        }
    }

    private static void OpenDriveProperties(string drivePath)
    {
        try
        {
            ShellHelper.ShowProperties(drivePath);
        }
        catch
        {
            /* best effort */
        }
    }

    private static void OpenRegistryKey(string fullPath)
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

        LaunchShellExecute("regedit.exe");
    }

    private static void LaunchMmcSnapIn(string snapInName)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "mmc.exe",
                Arguments = snapInName,
                UseShellExecute = true
            });
        }
        catch
        {
            /* best effort */
        }
    }

    private static void LaunchShellExecute(string fileName, string? arguments = null)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true
            });
        }
        catch
        {
            /* best effort */
        }
    }

    private static void LaunchViaExplorer(string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = arguments,
                UseShellExecute = true
            });
        }
        catch
        {
            /* best effort */
        }
    }
}