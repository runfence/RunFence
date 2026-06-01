using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.SecurityScanner;

public class MachineLevelPolicyScanner(
    IEnvironmentDataAccess environment,
    IFileSystemDataAccess fileSystem,
    ITaskSchedulerDataAccess taskScheduler,
    IGroupPolicyDataAccess groupPolicy,
    IServiceRegistryAccess serviceRegistry,
    IFirewallPolicyDataAccess firewallPolicy,
    IAccountPolicyDataAccess accountPolicy,
    AclCheckHelper aclCheck,
    PerUserScanner perUserScanner,
    ILoggingService log)
{
    private const int TaskActionExecute = 0;
    private const string NetworkConfigurationOperatorsSid = "S-1-5-32-556";

    public void ScanTaskScheduler(ScanContext ctx)
    {
        try
        {
            var tasks = taskScheduler.GetTaskSchedulerData();

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

                foreach (var action in task.Actions)
                {
                    if (action.ActionType != TaskActionExecute || string.IsNullOrWhiteSpace(action.Path))
                        continue;

                    var command = CommandLineParser.ResolveCommand(action.Path, action.Arguments, action.WorkingDirectory);
                    var context = new AutorunCommandContext(task.TaskPath, action.Arguments, action.WorkingDirectory, task.TaskPath);

                    if (!string.IsNullOrWhiteSpace(command.ExecutablePath))
                        SecurityScanner.AddAutorunPath(ctx.Autorun, command.ExecutablePath, taskOwnerExcluded,
                            StartupSecurityCategory.TaskScheduler, context);

                    if (!string.IsNullOrWhiteSpace(command.WrapperPayloadPath))
                    {
                        SecurityScanner.AddAutorunPath(ctx.Autorun, command.WrapperPayloadPath, taskOwnerExcluded,
                            StartupSecurityCategory.TaskScheduler, context);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.Error("Failed to scan Task Scheduler.", ex);
        }
    }

    public void ScanMachineGpScripts(ScanContext ctx)
    {
        try
        {
            foreach (var gpDir in new[] { groupPolicy.GetMachineGpScriptsDir(), groupPolicy.GetMachineGpUserScriptsDir() })
            {
                if (string.IsNullOrEmpty(gpDir) || !fileSystem.DirectoryExists(gpDir))
                    continue;
                ctx.AutorunLocationPaths.Add(gpDir);
                try
                {
                    var dirSecurity = fileSystem.GetDirectorySecurity(gpDir);
                    bool insecure = aclCheck.CheckContainerAcl(dirSecurity, gpDir, ctx.AdminSids,
                        StartupSecurityCategory.LogonScript, SecurityScanner.ContainerWriteRightsMask, ctx.Findings, ctx.Seen);
                    if (insecure)
                        ctx.InsecureContainers.Add(gpDir);
                }
                catch (Exception ex)
                {
                    log.Error($"Failed to read ACL for machine GP scripts dir '{gpDir}'.", ex);
                }
            }

            foreach (var scriptPath in groupPolicy.GetMachineGpScriptPaths())
            {
                if (!string.IsNullOrEmpty(scriptPath))
                    SecurityScanner.AddAutorunPath(ctx.Autorun, scriptPath, null, StartupSecurityCategory.LogonScript);
            }
        }
        catch (Exception ex)
        {
            log.Error("Failed to scan machine GP scripts.", ex);
        }
    }

    public void ScanPublicStartupFolder(ScanContext ctx)
    {
        var publicStartup = environment.GetPublicStartupPath();
        if (string.IsNullOrEmpty(publicStartup))
            return;
        ctx.AutorunLocationPaths.Add(publicStartup);

        perUserScanner.ScanStartupFolder(ctx, publicStartup, ctx.AdminSids, null);
    }

    public void ScanSharedWrapperScripts(ScanContext ctx)
    {
        try
        {
            var scriptsDir = groupPolicy.GetSharedWrapperScriptsDir();
            if (string.IsNullOrEmpty(scriptsDir) || !fileSystem.DirectoryExists(scriptsDir))
                return;

            ctx.AutorunLocationPaths.Add(scriptsDir);
            try
            {
                var dirSecurity = fileSystem.GetDirectorySecurity(scriptsDir);
                bool insecure = aclCheck.CheckContainerAcl(dirSecurity, scriptsDir, ctx.AdminSids,
                    StartupSecurityCategory.LogonScript, SecurityScanner.ContainerWriteRightsMask, ctx.Findings, ctx.Seen);
                if (insecure)
                    ctx.InsecureContainers.Add(scriptsDir);
            }
            catch (Exception ex)
            {
                log.Error($"Failed to read ACL for wrapper scripts dir '{scriptsDir}'.", ex);
            }

            foreach (var filePath in fileSystem.GetFilesInFolder(scriptsDir))
            {
                if (!string.IsNullOrEmpty(filePath))
                    SecurityScanner.AddAutorunPath(ctx.Autorun, filePath, null, StartupSecurityCategory.LogonScript);
            }
        }
        catch (Exception ex)
        {
            log.Error("Failed to scan shared wrapper scripts.", ex);
        }
    }

    public void ScanServices(ScanContext ctx)
    {
        try
        {
            foreach (var svc in serviceRegistry.GetAutoStartServices())
            {
                try
                {
                    if (svc.ServiceKeySecurity != null)
                    {
                        aclCheck.CheckRegistryKeyAcl(svc.ServiceKeySecurity, $@"HKLM\...\Services\{svc.ServiceName}",
                            ctx.AdminSids, SecurityScanner.ServiceRegistryWriteRightsMask,
                            StartupSecurityCategory.AutoStartService, ctx.Findings, ctx.Seen,
                            $@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\{svc.ServiceName}");
                    }

                    if (svc.ParametersKeySecurity != null)
                    {
                        var parametersExcluded = new HashSet<string>(ctx.AdminSids, StringComparer.OrdinalIgnoreCase)
                        {
                            NetworkConfigurationOperatorsSid
                        };
                        aclCheck.CheckRegistryKeyAcl(svc.ParametersKeySecurity, $@"HKLM\...\Services\{svc.ServiceName}\Parameters",
                            parametersExcluded, SecurityScanner.ServiceRegistryWriteRightsMask,
                            StartupSecurityCategory.AutoStartService, ctx.Findings, ctx.Seen,
                            $@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\{svc.ServiceName}\Parameters");
                    }
                }
                catch (Exception ex)
                {
                    log.Error($"Failed to check service key '{svc.ServiceName}'.", ex);
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
                        if (string.IsNullOrEmpty(parentDir) || !fileSystem.DirectoryExists(parentDir))
                            continue;

                        var dirSecurity = fileSystem.GetDirectorySecurity(parentDir);
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
                        log.Error($"Failed to check unquoted path candidate '{candidate}'.", ex);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.Error("Failed to scan services.", ex);
        }
    }

    public void ScanWindowsFirewall(ScanContext ctx)
    {
        try
        {
            var serviceState = firewallPolicy.GetWindowsFirewallServiceState();
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
            log.Error("Failed to check Windows Firewall service state.", ex);
        }

        try
        {
            var profiles = firewallPolicy.GetFirewallProfileStates();
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
            log.Error("Failed to check Windows Firewall profile states.", ex);
        }
    }

    public void ScanBlankPasswordPolicy(ScanContext ctx)
    {
        try
        {
            var enabled = accountPolicy.GetBlankPasswordRestrictionEnabled();
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
            log.Error("Failed to check blank password policy.", ex);
        }
    }

    public void ScanAccountLockoutPolicy(ScanContext ctx)
    {
        try
        {
            var threshold = accountPolicy.GetAccountLockoutThreshold();
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

            var adminLockoutEnabled = accountPolicy.GetAdminAccountLockoutEnabled();
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
            log.Error("Failed to check account lockout policy.", ex);
        }
    }
}
