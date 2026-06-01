using System.Security.AccessControl;
using Microsoft.Win32;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.SecurityScanner;

public class MachineLevelRegistryScanner(
    IAutorunRegistryAccess autorunRegistry,
    IWinlogonRegistryAccess winlogonRegistry,
    IIfeoRegistryAccess ifeoRegistry,
    IWindowsComponentRegistryAccess windowsComponentRegistry,
    IFileSystemDataAccess fileSystem,
    AclCheckHelper aclCheck,
    ILoggingService log)
{
    private const FileSystemRights MissingResolvedPathParentFileCreateRightsMask =
        FileSystemRights.WriteData |
        FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    private const FileSystemRights MissingResolvedPathAncestorDirectoryCreateRightsMask =
        FileSystemRights.AppendData |
        FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    private const RegistryRights IfeoRegistryWriteRightsMask =
        RegistryRights.SetValue | RegistryRights.CreateSubKey |
        RegistryRights.ChangePermissions | RegistryRights.TakeOwnership;
    private static readonly string s_windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    public void ScanMachineRegistryRunKeys(ScanContext ctx)
    {
        aclCheck.CheckRegistryKey(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", @"HKLM\...\Run",
            ctx.AdminSids, SecurityScanner.RunRegistryWriteRightsMask, StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen, ctx.Autorun, null,
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
        aclCheck.CheckRegistryKey(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", @"HKLM\...\RunOnce",
            ctx.AdminSids, SecurityScanner.RunRegistryWriteRightsMask, StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen, ctx.Autorun, null,
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce");

        foreach (var (path, display) in autorunRegistry.GetWow6432RunKeyPaths(null))
        {
            aclCheck.CheckRegistryKey(Registry.LocalMachine, path, display,
                ctx.AdminSids, SecurityScanner.RunRegistryWriteRightsMask, StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen, ctx.Autorun, null,
                @"HKEY_LOCAL_MACHINE\" + path);
        }
    }

    public void ScanWinlogon(ScanContext ctx)
    {
        try
        {
            var security = winlogonRegistry.GetWinlogonRegistryKeySecurity();
            if (security != null)
            {
                aclCheck.CheckRegistryKeyAcl(security, @"HKLM\...\Winlogon", ctx.AdminSids,
                    SecurityScanner.RunRegistryWriteRightsMask, StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen,
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon");
            }

            foreach (var exePath in winlogonRegistry.GetWinlogonExePaths())
            {
                if (!string.IsNullOrEmpty(exePath))
                    SecurityScanner.AddAutorunPath(ctx.Autorun, exePath, null, StartupSecurityCategory.RegistryRunKey);
            }
        }
        catch (Exception ex)
        {
            log.Error("Failed to check Winlogon.", ex);
        }
    }

    public void ScanAppInitDlls(ScanContext ctx)
    {
        try
        {
            foreach (var (security, displayPath, dllPaths) in winlogonRegistry.GetAppInitDllEntries())
            {
                if (security != null)
                {
                    var navTarget = displayPath.Contains("Wow6432Node")
                        ? @"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Microsoft\Windows NT\CurrentVersion\Windows"
                        : @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows";
                    aclCheck.CheckRegistryKeyAcl(security, displayPath, ctx.AdminSids,
                        SecurityScanner.RunRegistryWriteRightsMask, StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen,
                        navTarget);
                }

                foreach (var dll in dllPaths)
                {
                    if (!string.IsNullOrEmpty(dll))
                        SecurityScanner.AddAutorunPath(ctx.Autorun, dll, null, StartupSecurityCategory.RegistryRunKey);
                }
            }
        }
        catch (Exception ex)
        {
            log.Error("Failed to check AppInit_DLLs.", ex);
        }
    }

    public void ScanIfeo(ScanContext ctx)
    {
        try
        {
            var ifeoSecurity = ifeoRegistry.GetIfeoRegistryKeySecurity();
            if (ifeoSecurity != null)
            {
                aclCheck.CheckRegistryKeyAcl(ifeoSecurity, @"HKLM\...\Image File Execution Options",
                    ctx.AdminSids, IfeoRegistryWriteRightsMask,
                    StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen,
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options");
            }

            var ifeoWow = ifeoRegistry.GetIfeoWow6432RegistryKeySecurity();
            if (ifeoWow != null)
            {
                aclCheck.CheckRegistryKeyAcl(ifeoWow, @"HKLM\...\Wow6432Node\Image File Execution Options",
                    ctx.AdminSids, IfeoRegistryWriteRightsMask,
                    StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen,
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Microsoft\Windows NT\CurrentVersion\Image File Execution Options");
            }

            foreach (var subkey in ifeoRegistry.GetIfeoSubkeys())
            {
                if (subkey.Security != null)
                {
                    aclCheck.CheckRegistryKeyAcl(subkey.Security, subkey.DisplayPath,
                        ctx.AdminSids, IfeoRegistryWriteRightsMask,
                        StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen,
                        subkey.NavigationTarget);
                }

                if (!string.IsNullOrEmpty(subkey.DebuggerPath))
                    SecurityScanner.AddAutorunPath(ctx.Autorun, subkey.DebuggerPath, null, StartupSecurityCategory.RegistryRunKey);

                if (!string.IsNullOrEmpty(subkey.VerifierDlls))
                {
                    foreach (var dll in subkey.VerifierDlls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (!string.IsNullOrEmpty(dll))
                            SecurityScanner.AddAutorunPath(ctx.Autorun, dll, null, StartupSecurityCategory.RegistryRunKey);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.Error("Failed to scan IFEO.", ex);
        }
    }

    public void ScanSystemDllLocations(ScanContext ctx)
    {
        try
        {
            foreach (var (keyDisplayPath, security, dllPaths, navigationTarget) in windowsComponentRegistry.GetPrintMonitorEntries())
            {
                if (security != null)
                {
                    aclCheck.CheckRegistryKeyAcl(security, keyDisplayPath, ctx.AdminSids,
                        SecurityScanner.ServiceRegistryWriteRightsMask,
                        StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen,
                        navigationTarget);
                }

                AddSystemDllPaths(ctx, keyDisplayPath, navigationTarget, dllPaths);
            }
        }
        catch (Exception ex)
        {
            log.Error("Failed to scan Print Monitors.", ex);
        }

        try
        {
            foreach (var (security, dllPaths) in windowsComponentRegistry.GetLsaPackageEntries())
            {
                if (security != null)
                {
                    aclCheck.CheckRegistryKeyAcl(security, @"HKLM\...\Lsa", ctx.AdminSids,
                        SecurityScanner.ServiceRegistryWriteRightsMask,
                        StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen,
                        @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Lsa");
                }

                AddSystemDllPaths(ctx, @"HKLM\...\Lsa", @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Lsa", dllPaths);
            }
        }
        catch (Exception ex)
        {
            log.Error("Failed to scan LSA packages.", ex);
        }

        try
        {
            foreach (var (keyDisplayPath, security, dllPaths, navigationTarget) in windowsComponentRegistry.GetNetworkProviderEntries())
            {
                if (security != null)
                {
                    aclCheck.CheckRegistryKeyAcl(security, keyDisplayPath, ctx.AdminSids,
                        SecurityScanner.ServiceRegistryWriteRightsMask,
                        StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen,
                        navigationTarget);
                }

                AddSystemDllPaths(ctx, keyDisplayPath, navigationTarget, dllPaths);
            }
        }
        catch (Exception ex)
        {
            log.Error("Failed to scan Network Providers.", ex);
        }
    }

    private void AddSystemDllPaths(ScanContext ctx, string sourceDescription, string navigationTarget, IEnumerable<string> dllPaths)
    {
        foreach (var dll in dllPaths)
        {
            if (string.IsNullOrWhiteSpace(dll))
                continue;

            if (!IsBareModuleName(dll))
            {
                SecurityScanner.AddAutorunPath(ctx.Autorun, dll, null, StartupSecurityCategory.RegistryRunKey);
                continue;
            }

            var candidateName = Path.HasExtension(dll) ? dll : dll + ".dll";
            var resolvedPaths = GetBareModuleResolutionCandidates(candidateName);

            if (resolvedPaths.Count == 0)
            {
                SecurityScanner.AddAutorunWarning(ctx.Autorun, new AutorunWarning(
                    StartupSecurityCategory.RegistryRunKey,
                    $"{sourceDescription}: {dll}",
                    "Windows component DLL",
                    "Bare module name could not be resolved to a deterministic filesystem path.",
                    navigationTarget));
                continue;
            }

            foreach (var resolvedPath in resolvedPaths)
            {
                if (fileSystem.FileExists(resolvedPath))
                {
                    SecurityScanner.AddAutorunPath(ctx.Autorun, resolvedPath, null, StartupSecurityCategory.RegistryRunKey);
                    continue;
                }

                CheckMissingResolvedSystemDllPath(ctx, resolvedPath, navigationTarget);
            }
        }
    }

    private void CheckMissingResolvedSystemDllPath(ScanContext ctx, string resolvedPath, string navigationTarget)
    {
        var targetDirectory = Path.GetDirectoryName(resolvedPath);
        var existingDirectory = FindNearestExistingDirectory(targetDirectory);
        if (string.IsNullOrEmpty(existingDirectory))
            return;

        try
        {
            var dirSecurity = fileSystem.GetDirectorySecurity(existingDirectory);
            var requiredRights = string.Equals(existingDirectory, targetDirectory, StringComparison.OrdinalIgnoreCase)
                ? MissingResolvedPathParentFileCreateRightsMask
                : MissingResolvedPathAncestorDirectoryCreateRightsMask;
            var effective = aclCheck.ComputeFilteredFileRights(dirSecurity, ctx.AdminSids, requiredRights);
            foreach (var (sidStr, rights) in effective)
            {
                var writeRights = rights & requiredRights;
                if (writeRights == 0)
                    continue;

                var principal = aclCheck.CachedResolveDisplayName(sidStr);
                var key = (resolvedPath, sidStr);
                if (!ctx.Seen.Add(key))
                    continue;

                ctx.Findings.Add(new StartupSecurityFinding(
                    StartupSecurityCategory.RegistryRunKey,
                    resolvedPath,
                    sidStr,
                    principal,
                    $"{SecurityScanner.FormatFileSystemRights(writeRights, isDirectory: true)} on existing parent {existingDirectory}",
                    navigationTarget));
            }
        }
        catch (Exception ex)
        {
            log.Error($"Failed to check ACL for missing resolved DLL path '{resolvedPath}'.", ex);
        }
    }

    private static bool IsBareModuleName(string value)
    {
        var trimmed = SecurityScanner.ExpandEnvVars(value.Trim());
        return !Path.IsPathRooted(trimmed) &&
               trimmed.IndexOf('\\') < 0 &&
               trimmed.IndexOf('/') < 0;
    }

    private List<string> GetBareModuleResolutionCandidates(string candidateName)
    {
        return new List<string>
        {
            Path.Combine(s_windowsDir, "System32", candidateName),
            Path.Combine(s_windowsDir, "SysWOW64", candidateName),
        }
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    }

    private string? FindNearestExistingDirectory(string? path)
    {
        while (!string.IsNullOrEmpty(path))
        {
            if (fileSystem.DirectoryExists(path))
                return path;

            path = Path.GetDirectoryName(path);
        }

        return null;
    }
}
