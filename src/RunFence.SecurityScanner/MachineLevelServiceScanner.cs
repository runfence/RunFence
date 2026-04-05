using RunFence.Core.Models;

namespace RunFence.SecurityScanner;

public class MachineLevelServiceScanner(IScannerDataAccess dataAccess, AclCheckHelper aclCheck, PerUserScanner perUserScanner)
{
    public void ScanTaskScheduler(ScanContext ctx)
    {
        try
        {
            var tasks = dataAccess.GetTaskSchedulerData();

            foreach (var task in tasks)
            {
                HashSet<string>? taskOwnerExcluded = null;
                if (task is { IsPerUserTask: true, UserSid: not null })
                {
                    var taskExcluded = new HashSet<string>(ctx.AdminSids, StringComparer.OrdinalIgnoreCase) { task.UserSid };
                    if (!ctx.AdminSids.Contains(task.UserSid) && ctx.InteractiveUserSid != null)
                        taskExcluded.Add(ctx.InteractiveUserSid);
                    taskOwnerExcluded = taskExcluded;
                }

                foreach (var exePath in task.ExePaths)
                {
                    if (!string.IsNullOrEmpty(exePath))
                        SecurityScanner.AddAutorunPath(ctx.Autorun, exePath, taskOwnerExcluded, StartupSecurityCategory.TaskScheduler);
                }
            }
        }
        catch (Exception ex)
        {
            dataAccess.LogError($"Failed to scan Task Scheduler: {ex.Message}");
        }
    }

    public void ScanMachineGpScripts(ScanContext ctx)
    {
        try
        {
            foreach (var gpDir in new[] { dataAccess.GetMachineGpScriptsDir(), dataAccess.GetMachineGpUserScriptsDir() })
            {
                if (string.IsNullOrEmpty(gpDir) || !dataAccess.DirectoryExists(gpDir))
                    continue;
                ctx.AutorunLocationPaths.Add(gpDir);
                try
                {
                    var dirSecurity = dataAccess.GetDirectorySecurity(gpDir);
                    bool insecure = aclCheck.CheckContainerAcl(dirSecurity, gpDir, ctx.AdminSids,
                        StartupSecurityCategory.LogonScript, SecurityScanner.ContainerWriteRightsMask, ctx.Findings, ctx.Seen);
                    if (insecure)
                        ctx.InsecureContainers.Add(gpDir);
                }
                catch (Exception ex)
                {
                    dataAccess.LogError($"Failed to read ACL for machine GP scripts dir '{gpDir}': {ex.Message}");
                }
            }

            foreach (var scriptPath in dataAccess.GetMachineGpScriptPaths())
            {
                if (!string.IsNullOrEmpty(scriptPath))
                    SecurityScanner.AddAutorunPath(ctx.Autorun, scriptPath, null, StartupSecurityCategory.LogonScript);
            }
        }
        catch (Exception ex)
        {
            dataAccess.LogError($"Failed to scan machine GP scripts: {ex.Message}");
        }
    }

    public void ScanPublicStartupFolder(ScanContext ctx)
    {
        var publicStartup = dataAccess.GetPublicStartupPath();
        if (string.IsNullOrEmpty(publicStartup))
            return;
        ctx.AutorunLocationPaths.Add(publicStartup);

        perUserScanner.ScanStartupFolder(publicStartup, ctx.AdminSids, null, ctx.Findings, ctx.Seen, ctx.InsecureContainers, ctx.Autorun);
    }

    public void ScanSharedWrapperScripts(ScanContext ctx)
    {
        try
        {
            var scriptsDir = dataAccess.GetSharedWrapperScriptsDir();
            if (string.IsNullOrEmpty(scriptsDir) || !dataAccess.DirectoryExists(scriptsDir))
                return;

            ctx.AutorunLocationPaths.Add(scriptsDir);
            try
            {
                var dirSecurity = dataAccess.GetDirectorySecurity(scriptsDir);
                bool insecure = aclCheck.CheckContainerAcl(dirSecurity, scriptsDir, ctx.AdminSids,
                    StartupSecurityCategory.LogonScript, SecurityScanner.ContainerWriteRightsMask, ctx.Findings, ctx.Seen);
                if (insecure)
                    ctx.InsecureContainers.Add(scriptsDir);
            }
            catch (Exception ex)
            {
                dataAccess.LogError($"Failed to read ACL for wrapper scripts dir '{scriptsDir}': {ex.Message}");
            }

            foreach (var filePath in dataAccess.GetFilesInFolder(scriptsDir))
            {
                if (!string.IsNullOrEmpty(filePath))
                    SecurityScanner.AddAutorunPath(ctx.Autorun, filePath, null, StartupSecurityCategory.LogonScript);
            }
        }
        catch (Exception ex)
        {
            dataAccess.LogError($"Failed to scan shared wrapper scripts: {ex.Message}");
        }
    }

    public void ScanServices(ScanContext ctx)
    {
        try
        {
            foreach (var svc in dataAccess.GetAutoStartServices())
            {
                try
                {
                    var security = dataAccess.GetServiceRegistryKeySecurity(svc.ServiceName);
                    if (security != null)
                    {
                        aclCheck.CheckRegistryKeyAcl(security, $@"HKLM\...\Services\{svc.ServiceName}",
                            ctx.AdminSids, SecurityScanner.ServiceRegistryWriteRightsMask,
                            StartupSecurityCategory.AutoStartService, ctx.Findings, ctx.Seen,
                            $@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\{svc.ServiceName}");
                    }
                }
                catch (Exception ex)
                {
                    dataAccess.LogError($"Failed to check service key '{svc.ServiceName}': {ex.Message}");
                }

                if (!string.IsNullOrEmpty(svc.ExpandedImagePath))
                    SecurityScanner.AddAutorunPath(ctx.Autorun, svc.ExpandedImagePath, null, StartupSecurityCategory.AutoStartService);

                if (!string.IsNullOrEmpty(svc.ServiceDllPath))
                    SecurityScanner.AddAutorunPath(ctx.Autorun, svc.ServiceDllPath, null, StartupSecurityCategory.AutoStartService);

                var candidates = CommandLineParser.ComputeUnquotedPathCandidates(svc.ImagePath);
                foreach (var candidate in candidates)
                {
                    try
                    {
                        var parentDir = Path.GetDirectoryName(candidate);
                        if (string.IsNullOrEmpty(parentDir) || !dataAccess.DirectoryExists(parentDir))
                            continue;

                        var dirSecurity = dataAccess.GetDirectorySecurity(parentDir);
                        var effective = aclCheck.ComputeFilteredFileRights(dirSecurity, ctx.AdminSids, SecurityScanner.ContainerWriteRightsMask);
                        foreach (var (sidStr, rights) in effective)
                        {
                            var writeRights = rights & SecurityScanner.ContainerWriteRightsMask;
                            if (writeRights == 0)
                                continue;

                            var principal = aclCheck.CachedResolveDisplayName(sidStr);
                            var targetDesc = $"{svc.ExpandedImagePath} (unquoted path: {parentDir})";
                            var key = (targetDesc, sidStr);
                            if (!ctx.Seen.Add(key))
                                continue;
                            ctx.Findings.Add(new StartupSecurityFinding(
                                StartupSecurityCategory.AutoStartService,
                                targetDesc, sidStr, principal, SecurityScanner.FormatFileSystemRights(writeRights, isDirectory: true), parentDir));
                        }
                    }
                    catch (Exception ex)
                    {
                        dataAccess.LogError($"Failed to check unquoted path candidate '{candidate}': {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            dataAccess.LogError($"Failed to scan services: {ex.Message}");
        }
    }

    public void ScanWindowsFirewall(ScanContext ctx)
    {
        try
        {
            var serviceState = dataAccess.GetWindowsFirewallServiceState();
            if (serviceState != null)
            {
                var (isDisabled, isStopped) = serviceState.Value;
                if (isDisabled)
                {
                    ctx.Findings.Add(new StartupSecurityFinding(
                        StartupSecurityCategory.FirewallPolicy,
                        "Windows Firewall service (MpsSvc) is disabled",
                        "", "All network connections",
                        "Windows Firewall will not start, leaving the system unprotected",
                        "services.msc"));
                    return;
                }

                if (isStopped)
                {
                    ctx.Findings.Add(new StartupSecurityFinding(
                        StartupSecurityCategory.FirewallPolicy,
                        "Windows Firewall service (MpsSvc) is not running",
                        "", "All network connections",
                        "Windows Firewall is currently stopped, leaving the system unprotected",
                        "services.msc"));
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            dataAccess.LogError($"Failed to check Windows Firewall service state: {ex.Message}");
        }

        try
        {
            var profiles = dataAccess.GetFirewallProfileStates();
            if (profiles == null)
                return;

            foreach (var (profileName, enabled) in profiles)
            {
                if (!enabled)
                {
                    ctx.Findings.Add(new StartupSecurityFinding(
                        StartupSecurityCategory.FirewallPolicy,
                        $"Windows Firewall is disabled ({profileName} profile)",
                        "", "All network connections",
                        "Inbound connections are unrestricted",
                        "wf.msc"));
                }
            }
        }
        catch (Exception ex)
        {
            dataAccess.LogError($"Failed to check Windows Firewall profile states: {ex.Message}");
        }
    }

    public void ScanBlankPasswordPolicy(ScanContext ctx)
    {
        try
        {
            var enabled = dataAccess.GetBlankPasswordRestrictionEnabled();
            if (enabled == false)
            {
                ctx.Findings.Add(new StartupSecurityFinding(
                    StartupSecurityCategory.AccountPolicy,
                    "Blank password restriction is disabled (LimitBlankPasswordUse = 0)",
                    "", "All local accounts",
                    "Accounts with blank passwords can be used for network logon",
                    "secpol.msc"));
            }
        }
        catch (Exception ex)
        {
            dataAccess.LogError($"Failed to check blank password policy: {ex.Message}");
        }
    }

    public void ScanAccountLockoutPolicy(ScanContext ctx)
    {
        try
        {
            var threshold = dataAccess.GetAccountLockoutThreshold();
            if (threshold is null)
                return;

            if (threshold == 0)
            {
                ctx.Findings.Add(new StartupSecurityFinding(
                    StartupSecurityCategory.AccountPolicy,
                    "Account lockout is not configured (threshold = 0)",
                    "", "All local accounts",
                    "No lockout on failed password attempts",
                    "secpol.msc"));
                return;
            }

            var adminLockoutEnabled = dataAccess.GetAdminAccountLockoutEnabled();
            if (adminLockoutEnabled == false)
            {
                ctx.Findings.Add(new StartupSecurityFinding(
                    StartupSecurityCategory.AccountPolicy,
                    "Administrator accounts are exempt from account lockout",
                    "", "Administrator accounts",
                    "Lockout policy does not apply to administrators, allowing brute-force attacks",
                    "secpol.msc"));
            }
        }
        catch (Exception ex)
        {
            dataAccess.LogError($"Failed to check account lockout policy: {ex.Message}");
        }
    }
}