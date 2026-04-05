using System.Security.AccessControl;
using Microsoft.Win32;
using RunFence.Core.Models;

namespace RunFence.SecurityScanner;

public class MachineLevelRegistryScanner(IScannerDataAccess dataAccess, AclCheckHelper aclCheck)
{
    private const RegistryRights IfeoRegistryWriteRightsMask =
        RegistryRights.SetValue | RegistryRights.CreateSubKey |
        RegistryRights.ChangePermissions | RegistryRights.TakeOwnership;

    public void ScanMachineRegistryRunKeys(ScanContext ctx)
    {
        aclCheck.CheckRegistryKey(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", @"HKLM\...\Run",
            ctx.AdminSids, SecurityScanner.RunRegistryWriteRightsMask, StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen, ctx.Autorun, null,
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
        aclCheck.CheckRegistryKey(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", @"HKLM\...\RunOnce",
            ctx.AdminSids, SecurityScanner.RunRegistryWriteRightsMask, StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen, ctx.Autorun, null,
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce");

        foreach (var (path, display) in dataAccess.GetWow6432RunKeyPaths(null))
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
            var security = dataAccess.GetWinlogonRegistryKeySecurity();
            if (security != null)
            {
                aclCheck.CheckRegistryKeyAcl(security, @"HKLM\...\Winlogon", ctx.AdminSids,
                    SecurityScanner.RunRegistryWriteRightsMask, StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen,
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon");
            }

            foreach (var exePath in dataAccess.GetWinlogonExePaths())
            {
                if (!string.IsNullOrEmpty(exePath))
                    SecurityScanner.AddAutorunPath(ctx.Autorun, exePath, null, StartupSecurityCategory.RegistryRunKey);
            }
        }
        catch (Exception ex)
        {
            dataAccess.LogError($"Failed to check Winlogon: {ex.Message}");
        }
    }

    public void ScanAppInitDlls(ScanContext ctx)
    {
        try
        {
            foreach (var (security, displayPath, dllPaths) in dataAccess.GetAppInitDllEntries())
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
            dataAccess.LogError($"Failed to check AppInit_DLLs: {ex.Message}");
        }
    }

    public void ScanIfeo(ScanContext ctx)
    {
        try
        {
            var ifeoSecurity = dataAccess.GetIfeoRegistryKeySecurity();
            if (ifeoSecurity != null)
            {
                aclCheck.CheckRegistryKeyAcl(ifeoSecurity, @"HKLM\...\Image File Execution Options",
                    ctx.AdminSids, IfeoRegistryWriteRightsMask,
                    StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen,
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options");
            }

            var ifeoWow = dataAccess.GetIfeoWow6432RegistryKeySecurity();
            if (ifeoWow != null)
            {
                aclCheck.CheckRegistryKeyAcl(ifeoWow, @"HKLM\...\Wow6432Node\Image File Execution Options",
                    ctx.AdminSids, IfeoRegistryWriteRightsMask,
                    StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen,
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Microsoft\Windows NT\CurrentVersion\Image File Execution Options");
            }

            foreach (var exeName in dataAccess.GetIfeoSubkeyNames())
            {
                var debugger = dataAccess.GetIfeoDebuggerPath(exeName);
                if (!string.IsNullOrEmpty(debugger))
                    SecurityScanner.AddAutorunPath(ctx.Autorun, debugger, null, StartupSecurityCategory.RegistryRunKey);

                var verifierDlls = dataAccess.GetIfeoVerifierDlls(exeName);
                if (!string.IsNullOrEmpty(verifierDlls))
                {
                    foreach (var dll in verifierDlls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (!string.IsNullOrEmpty(dll))
                            SecurityScanner.AddAutorunPath(ctx.Autorun, dll, null, StartupSecurityCategory.RegistryRunKey);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            dataAccess.LogError($"Failed to scan IFEO: {ex.Message}");
        }
    }

    public void ScanSystemDllLocations(ScanContext ctx)
    {
        try
        {
            foreach (var (keyDisplayPath, security, dllPaths, navigationTarget) in dataAccess.GetPrintMonitorEntries())
            {
                if (security != null)
                {
                    aclCheck.CheckRegistryKeyAcl(security, keyDisplayPath, ctx.AdminSids,
                        SecurityScanner.ServiceRegistryWriteRightsMask,
                        StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen,
                        navigationTarget);
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
            dataAccess.LogError($"Failed to scan Print Monitors: {ex.Message}");
        }

        try
        {
            foreach (var (security, dllPaths) in dataAccess.GetLsaPackageEntries())
            {
                if (security != null)
                {
                    aclCheck.CheckRegistryKeyAcl(security, @"HKLM\...\Lsa", ctx.AdminSids,
                        SecurityScanner.ServiceRegistryWriteRightsMask,
                        StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen,
                        @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Lsa");
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
            dataAccess.LogError($"Failed to scan LSA packages: {ex.Message}");
        }

        try
        {
            foreach (var (keyDisplayPath, security, dllPaths, navigationTarget) in dataAccess.GetNetworkProviderEntries())
            {
                if (security != null)
                {
                    aclCheck.CheckRegistryKeyAcl(security, keyDisplayPath, ctx.AdminSids,
                        SecurityScanner.ServiceRegistryWriteRightsMask,
                        StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen,
                        navigationTarget);
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
            dataAccess.LogError($"Failed to scan Network Providers: {ex.Message}");
        }
    }
}