using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch;

namespace RunFence.Account.UI;

/// <summary>
/// Installs packages for an account by launching a PowerShell script via <see cref="ILaunchFacade"/>,
/// and detects installed packages for a given account SID.
/// </summary>
public class PackageInstallService(ILaunchFacade launchFacade, AccountToolResolver toolResolver, ILoggingService log)
{
    public bool IsPackageInstalled(InstallablePackage package, string sid)
    {
        if (package.DetectExeName != null)
            return toolResolver.ResolveWindowsAppsExe(sid, package.DetectExeName) != null;

        if (package.DetectProfileRelativePath != null)
        {
            var profileRoot = toolResolver.GetProfileRoot(sid);
            return profileRoot != null && File.Exists(Path.Combine(profileRoot, package.DetectProfileRelativePath));
        }

        return false;
    }

    public void InstallPackages(IReadOnlyList<InstallablePackage> packages, AccountLaunchIdentity identity)
    {
        if (packages.Count == 0)
            return;
        var body = string.Join("\n", packages.Select(p => p.PowerShellCommand));

        var cmd = $"try {{\n{body}\n}} finally {{\n" +
                  "New-Item -Path \"$env:TEMP\\runfence-install-done.marker\" -Force | Out-Null\n" +
                  "Remove-Item $PSCommandPath -Force -ErrorAction SilentlyContinue\n}";

        if (GetMarkerPath(identity.Sid, out var markerPath))
        {
            try
            {
                File.Delete(markerPath);
            }
            catch
            {
            }
        }

        var scriptPath = WriteInstallScript(cmd, identity.Sid);
        try
        {
            var powershellTarget = new ProcessLaunchTarget("powershell.exe",
                Arguments: CommandLineHelper.JoinArgs(["-NoExit", "-ExecutionPolicy", "Bypass", "-File", scriptPath]));
            launchFacade.LaunchFile(powershellTarget, identity, permissionPrompt: (_, _) => true);
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
        if (!GetMarkerPath(sid, out var markerPath))
            return;

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

    private bool GetMarkerPath(string sid, out string markerPath)
    {
        var profilePath = toolResolver.GetProfileRoot(sid);
        if (profilePath == null)
        {
            markerPath = null!;
            return false;
        }

        markerPath = Path.Combine(profilePath, "AppData", "Local", "Temp", "runfence-install-done.marker");
        return true;
    }

    private string WriteInstallScript(string command, string userSid)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RunFence");
        Directory.CreateDirectory(dir);

        foreach (var stale in Directory.GetFiles(dir, "install-*.ps1"))
            try
            {
                if (File.GetCreationTimeUtc(stale) > DateTime.UtcNow.AddHours(-1)) continue;
                File.Delete(stale);
            }
            catch
            {
            }

        var scriptPath = Path.Combine(dir, $"install-{Guid.NewGuid():N}.ps1");
        CreateScriptFileWithRestrictedAccess(scriptPath, command, userSid);
        return scriptPath;
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private void CreateScriptFileWithRestrictedAccess(string filePath, string command, string userSid)
    {
        try
        {
            var security = new FileSecurity();
            security.SetAccessRuleProtection(true, false);

            var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            security.AddAccessRule(new FileSystemAccessRule(
                administrators, FileSystemRights.FullControl, AccessControlType.Allow));

            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            security.AddAccessRule(new FileSystemAccessRule(
                system, FileSystemRights.FullControl, AccessControlType.Allow));

            var user = new SecurityIdentifier(userSid);
            security.AddAccessRule(new FileSystemAccessRule(
                user, FileSystemRights.ReadAndExecute | FileSystemRights.Delete, AccessControlType.Allow));

            var fileInfo = new FileInfo(filePath);
            using var fs = fileInfo.Create(FileMode.CreateNew, FileSystemRights.Write | FileSystemRights.ReadData,
                FileShare.None, 4096, FileOptions.None, security);
            using var writer = new StreamWriter(fs, System.Text.Encoding.UTF8);
            writer.Write(command);
        }
        catch (Exception ex)
        {
            log.Error("Failed to create restricted script file", ex);
            TryDeleteFile(filePath);
            throw new InvalidOperationException("Failed to secure install script", ex);
        }
    }
}
