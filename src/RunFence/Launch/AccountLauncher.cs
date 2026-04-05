using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using RunFence.Account;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Launch;

public class AccountLauncher(
    IProcessLaunchService processLaunchService,
    IPermissionGrantService permissionGrantService,
    ISidResolver sidResolver)
    : IAccountLauncher
{
    public void LaunchCmd(SecureString? password, CredentialEntry credEntry,
        IReadOnlyDictionary<string, string>? sidNames, LaunchFlags flags = default)
    {
        var creds = LaunchCredentials.FromCredentialEntry(password, credEntry, sidResolver, sidNames);
        var terminalExe = ResolveTerminalExe(credEntry.Sid);
        var profilePath = GetProfileRoot(credEntry.Sid);
        processLaunchService.LaunchExe(new ProcessLaunchTarget(terminalExe, WorkingDirectory: profilePath), creds, flags);
    }

    public void LaunchEnvironmentVariables(SecureString? password, CredentialEntry credEntry,
        IReadOnlyDictionary<string, string>? sidNames, LaunchFlags flags = default)
    {
        var creds = LaunchCredentials.FromCredentialEntry(password, credEntry, sidResolver, sidNames);
        processLaunchService.LaunchExe(new ProcessLaunchTarget("rundll32.exe", Arguments: "sysdm.cpl,EditEnvironmentVariables"), creds, flags);
    }

    /// <summary>
    /// Returns wt.exe from the account's WindowsApps directory if installed, otherwise falls back to cmd.exe.
    /// </summary>
    public string ResolveTerminalExe(string sid)
        => ResolveWindowsAppsExe(sid, "wt.exe") ?? "cmd.exe";

    public bool IsPackageInstalled(InstallablePackage package, string sid)
    {
        if (package.DetectExeName != null)
            return ResolveWindowsAppsExe(sid, package.DetectExeName) != null;

        if (package.DetectProfileRelativePath != null)
        {
            var profileRoot = GetProfileRoot(sid);
            return profileRoot != null && File.Exists(Path.Combine(profileRoot, package.DetectProfileRelativePath));
        }

        return false;
    }

    public void InstallPackage(InstallablePackage package, SecureString? password, CredentialEntry credEntry,
        IReadOnlyDictionary<string, string>? sidNames, LaunchFlags flags = default)
        => InstallPackages([package], password, credEntry, sidNames, flags);

    public void InstallPackages(IReadOnlyList<InstallablePackage> packages, SecureString? password, CredentialEntry credEntry,
        IReadOnlyDictionary<string, string>? sidNames, LaunchFlags flags = default)
    {
        if (packages.Count == 0)
            return;
        var creds = LaunchCredentials.FromCredentialEntry(password, credEntry, sidResolver, sidNames);
        var cmd = string.Join("\n", packages.Select(p => p.PowerShellCommand));

        // Append sentinel marker so WaitForInstallCompletionAsync can detect script completion,
        // then self-delete (user has ReadAndExecute+Delete but no Write on this file).
        cmd += "\nNew-Item -Path \"$env:TEMP\\runfence-install-done.marker\" -Force | Out-Null";
        cmd += "\nRemove-Item $PSCommandPath -Force -ErrorAction SilentlyContinue";

        // CreateProcessWithLogonW has a 1024-char command line limit.
        // Write to a temp script file to avoid hitting it.
        var scriptPath = WriteInstallScript(cmd, credEntry.Sid);
        try
        {
            processLaunchService.LaunchExe(
                new ProcessLaunchTarget("powershell.exe", Arguments: CommandLineHelper.JoinArgs(["-NoExit", "-ExecutionPolicy", "Bypass", "-File", scriptPath])),
                creds, flags);
        }
        catch
        {
            TryDeleteFile(scriptPath);
            throw;
        }
    }

    /// <summary>
    /// Waits for the install script launched by <see cref="InstallPackages"/> to complete by polling
    /// for a sentinel marker file written at the end of the script. Deletes the marker on detection.
    /// Returns when the marker is found or when <paramref name="timeout"/> elapses.
    /// </summary>
    public async Task WaitForInstallCompletionAsync(string sid, TimeSpan timeout)
    {
        var profilePath = GetProfilePath(sid);
        if (profilePath == null)
            return;

        var markerPath = Path.Combine(profilePath, "AppData", "Local", "Temp", "runfence-install-done.marker");
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(markerPath))
            {
                TryDeleteFile(markerPath);
                return;
            }

            await Task.Delay(1000);
        }
    }

    private static string? GetProfilePath(string sid)
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{sid}");
        return key?.GetValue("ProfileImagePath") as string;
    }

    private static string WriteInstallScript(string command, string userSid)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RunFence");
        Directory.CreateDirectory(dir);

        // Clean up stale install scripts from previous runs
        foreach (var stale in Directory.GetFiles(dir, "install-*.ps1"))
            try
            {
                File.Delete(stale);
            }
            catch
            {
                /* in use or already gone */
            }

        var scriptPath = Path.Combine(dir, $"install-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, command);
        RestrictScriptFileAccess(scriptPath, userSid);
        return scriptPath;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            /* best effort */
        }
    }

    private static void RestrictScriptFileAccess(string filePath, string userSid)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var security = fileInfo.GetAccessControl();

            security.SetAccessRuleProtection(true, false);

            var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            security.AddAccessRule(new FileSystemAccessRule(
                administrators, FileSystemRights.FullControl, AccessControlType.Allow));

            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            security.AddAccessRule(new FileSystemAccessRule(
                system, FileSystemRights.FullControl, AccessControlType.Allow));

            // Grant the target user read+execute+delete so PowerShell can run and self-delete the script,
            // but not write (preventing script tampering).
            var user = new SecurityIdentifier(userSid);
            security.AddAccessRule(new FileSystemAccessRule(
                user, FileSystemRights.ReadAndExecute | FileSystemRights.Delete, AccessControlType.Allow));

            fileInfo.SetAccessControl(security);
        }
        catch
        {
            /* defense-in-depth: do not fail the install if ACL restriction fails */
        }
    }

    private string? GetProfileRoot(string sid)
        => SidResolutionHelper.GetCurrentUserSid() == sid
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : sidResolver.TryGetProfilePath(sid);

    private string? ResolveWindowsAppsExe(string sid, string exeName)
    {
        var localAppData = SidResolutionHelper.GetCurrentUserSid() == sid
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : sidResolver.TryGetProfilePath(sid) is { } profilePath
                ? Path.Combine(profilePath, "AppData", "Local")
                : null;

        if (localAppData == null)
            return null;
        var exePath = Path.Combine(localAppData, "Microsoft", "WindowsApps", exeName);
        return File.Exists(exePath) ? exePath : null;
    }

    public bool LaunchFolderBrowser(
        SecureString? password, CredentialEntry credEntry, AppSettings settings, IReadOnlyDictionary<string, string>? sidNames,
        LaunchFlags flags = default,
        Func<string, string, bool>? confirm = null)
    {
        var creds = LaunchCredentials.FromCredentialEntry(password, credEntry, sidResolver, sidNames);

        var startMenuPath = sidResolver.TryGetStartMenuProgramsPath(credEntry.Sid, credEntry.IsCurrentAccount)
                            ?? throw new InvalidOperationException($"Profile path not found in registry for SID {credEntry.Sid}.");

        var folderBrowserExe = PathHelper.ResolveExePath(settings.FolderBrowserExePath);
        var folderBrowserArgs = settings.FolderBrowserArguments;

        bool grantsAdded = false;
        if (!credEntry.IsCurrentAccount && !string.IsNullOrEmpty(folderBrowserExe) && File.Exists(folderBrowserExe))
            grantsAdded = permissionGrantService.EnsureExeDirectoryAccess(folderBrowserExe, credEntry.Sid, confirm).DatabaseModified;

        processLaunchService.LaunchFolder(startMenuPath, folderBrowserExe, folderBrowserArgs, creds, flags);
        return grantsAdded;
    }
}