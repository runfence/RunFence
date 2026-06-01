using Microsoft.Win32;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.SecurityScanner;

public class PerUserScanner(
    IEnvironmentDataAccess environment,
    IFileSystemDataAccess fileSystem,
    IAutorunRegistryAccess autorunRegistry,
    IGroupPolicyDataAccess groupPolicy,
    AclCheckHelper aclCheck,
    AutorunChecker autorunChecker,
    ILoggingService log)
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOnceKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";

    public void ScanPerUserLocations(ScanContext ctx,
        List<(string Sid, string? ProfilePath)> allProfiles,
        CancellationToken ct)
    {
        bool skipInteractive = ctx.InteractiveUserSid != null &&
                               string.Equals(ctx.InteractiveUserSid, ctx.CurrentUserSid, StringComparison.OrdinalIgnoreCase);

        var excludedForCurrentUser = BuildPerUserExcluded(ctx.AdminSids, ctx.CurrentUserSid, ctx.InteractiveUserSid);

        // Current user startup folder
        var currentUserStartup = environment.GetCurrentUserStartupPath();
        if (!string.IsNullOrEmpty(currentUserStartup))
        {
            ctx.AutorunLocationPaths.Add(currentUserStartup);
            ScanStartupFolder(ctx, currentUserStartup, excludedForCurrentUser, excludedForCurrentUser);
        }

        // Current user logon scripts
        if (ctx.CurrentUserSid != null)
        {
            ScanUserLogonScripts(ctx, ctx.CurrentUserSid, excludedForCurrentUser);
        }

        // Interactive user startup folder and logon scripts
        if (!skipInteractive && ctx.InteractiveUserSid != null)
        {
            var excludedForInteractive = BuildPerUserExcluded(ctx.AdminSids, ctx.InteractiveUserSid, ctx.InteractiveUserSid);

            var profilePath = environment.GetInteractiveUserProfilePath(ctx.InteractiveUserSid);
            if (!string.IsNullOrEmpty(profilePath))
            {
                var interactiveStartup = Path.Combine(profilePath,
                    @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup");
                ctx.AutorunLocationPaths.Add(interactiveStartup);
                ScanStartupFolder(ctx, interactiveStartup, excludedForInteractive, excludedForInteractive);
            }

            ScanUserLogonScripts(ctx, ctx.InteractiveUserSid, excludedForInteractive);
        }

        ct.ThrowIfCancellationRequested();

        ScanPerUserRegistryRunKeys(ctx, skipInteractive, excludedForCurrentUser);

        ct.ThrowIfCancellationRequested();

        ScanAllUserProfiles(ctx, allProfiles, ct);
    }

    private void ScanPerUserRegistryRunKeys(ScanContext ctx, bool skipInteractive, HashSet<string> hkcuExcluded)
    {
        aclCheck.CheckRegistryKey(Registry.CurrentUser, RunKeyPath, @"HKCU\...\Run",
            hkcuExcluded, SecurityScanner.RunRegistryWriteRightsMask, StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen, ctx.Autorun, hkcuExcluded,
            @"HKEY_CURRENT_USER\" + RunKeyPath);
        aclCheck.CheckRegistryKey(Registry.CurrentUser, RunOnceKeyPath, @"HKCU\...\RunOnce",
            hkcuExcluded, SecurityScanner.RunRegistryWriteRightsMask, StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen, ctx.Autorun, hkcuExcluded,
            @"HKEY_CURRENT_USER\" + RunOnceKeyPath);

        foreach (var (path, display) in autorunRegistry.GetWow6432RunKeyPaths(ctx.CurrentUserSid))
        {
            if (!path.StartsWith(ctx.CurrentUserSid ?? "---", StringComparison.OrdinalIgnoreCase))
                continue;
            aclCheck.CheckRegistryKey(Registry.Users, path, display,
                hkcuExcluded, SecurityScanner.RunRegistryWriteRightsMask, StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen, ctx.Autorun, hkcuExcluded,
                @"HKEY_USERS\" + path);
        }

        if (!skipInteractive && ctx.InteractiveUserSid != null)
        {
            var hkuExcluded = BuildPerUserExcluded(ctx.AdminSids, ctx.InteractiveUserSid, ctx.InteractiveUserSid);
            var hkuRunPath = $@"{ctx.InteractiveUserSid}\{RunKeyPath}";
            var hkuRunOncePath = $@"{ctx.InteractiveUserSid}\{RunOnceKeyPath}";
            aclCheck.CheckRegistryKey(Registry.Users, hkuRunPath, $@"HKU\{ctx.InteractiveUserSid}\...\Run",
                hkuExcluded, SecurityScanner.RunRegistryWriteRightsMask, StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen, ctx.Autorun, hkuExcluded,
                @"HKEY_USERS\" + hkuRunPath);
            aclCheck.CheckRegistryKey(Registry.Users, hkuRunOncePath, $@"HKU\{ctx.InteractiveUserSid}\...\RunOnce",
                hkuExcluded, SecurityScanner.RunRegistryWriteRightsMask, StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen, ctx.Autorun, hkuExcluded,
                @"HKEY_USERS\" + hkuRunOncePath);

            foreach (var (path, display) in autorunRegistry.GetWow6432RunKeyPaths(ctx.InteractiveUserSid))
            {
                if (!path.StartsWith(ctx.InteractiveUserSid, StringComparison.OrdinalIgnoreCase))
                    continue;
                aclCheck.CheckRegistryKey(Registry.Users, path, display,
                    hkuExcluded, SecurityScanner.RunRegistryWriteRightsMask, StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen, ctx.Autorun, hkuExcluded,
                    @"HKEY_USERS\" + path);
            }
        }
    }

    private void ScanAllUserProfiles(ScanContext ctx,
        List<(string Sid, string? ProfilePath)> allProfiles,
        CancellationToken ct)
    {
        try
        {
            foreach (var (userSid, profilePath) in allProfiles)
            {
                ct.ThrowIfCancellationRequested();

                if (string.Equals(userSid, ctx.CurrentUserSid, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(userSid, ctx.InteractiveUserSid, StringComparison.OrdinalIgnoreCase))
                    continue;

                var excluded = BuildPerUserExcluded(ctx.AdminSids, userSid, ctx.InteractiveUserSid);

                if (!string.IsNullOrEmpty(profilePath))
                {
                    var startupPath = Path.Combine(profilePath,
                        @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup");
                    if (fileSystem.DirectoryExists(startupPath))
                    {
                        ctx.AutorunLocationPaths.Add(startupPath);
                        ScanStartupFolder(ctx, startupPath, excluded, excluded);
                    }
                }

                try
                {
                    var hkuRunPath = $@"{userSid}\{RunKeyPath}";
                    aclCheck.CheckRegistryKey(Registry.Users, hkuRunPath, $@"HKU\{userSid}\...\Run",
                        excluded, SecurityScanner.RunRegistryWriteRightsMask, StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen, ctx.Autorun, excluded,
                        @"HKEY_USERS\" + hkuRunPath);
                }
                catch
                {
                    // Unloaded profiles are intentionally skipped; this scanner never loads HKU hives as a side effect.
                    /* HKU hive not loaded */
                }

                try
                {
                    var hkuRunOncePath = $@"{userSid}\{RunOnceKeyPath}";
                    aclCheck.CheckRegistryKey(Registry.Users, hkuRunOncePath, $@"HKU\{userSid}\...\RunOnce",
                        excluded, SecurityScanner.RunRegistryWriteRightsMask, StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen, ctx.Autorun, excluded,
                        @"HKEY_USERS\" + hkuRunOncePath);
                }
                catch
                {
                    // Unloaded profiles are intentionally skipped; this scanner never loads HKU hives as a side effect.
                    /* HKU hive not loaded */
                }

                foreach (var (path, display) in autorunRegistry.GetWow6432RunKeyPaths(userSid))
                {
                    if (!path.StartsWith(userSid, StringComparison.OrdinalIgnoreCase))
                        continue;
                    try
                    {
                        aclCheck.CheckRegistryKey(Registry.Users, path, display,
                            excluded, SecurityScanner.RunRegistryWriteRightsMask, StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen, ctx.Autorun, excluded,
                            @"HKEY_USERS\" + path);
                    }
                    catch
                    {
                        // Unloaded profiles are intentionally skipped; this scanner never loads HKU hives as a side effect.
                        /* HKU hive not loaded */
                    }
                }

                ScanUserLogonScripts(ctx, userSid, excluded);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.Error("Failed to enumerate user profiles.", ex);
        }
    }

    private void ScanUserLogonScripts(ScanContext ctx, string userSid, HashSet<string> excludedSids)
    {
        try
        {
            var gpDir = groupPolicy.GetGpScriptsDir(userSid);
            if (!string.IsNullOrEmpty(gpDir) && fileSystem.DirectoryExists(gpDir))
            {
                ctx.AutorunLocationPaths.Add(gpDir);
                try
                {
                    var dirSecurity = fileSystem.GetDirectorySecurity(gpDir);
                    bool insecure = aclCheck.CheckContainerAcl(dirSecurity, gpDir, excludedSids,
                        StartupSecurityCategory.LogonScript, SecurityScanner.ContainerWriteRightsMask, ctx.Findings, ctx.Seen);
                    if (insecure)
                        ctx.InsecureContainers.Add(gpDir);
                }
                catch (Exception ex)
                {
                    log.Error($"Failed to read ACL for GP scripts dir '{gpDir}'.", ex);
                }
            }

            foreach (var scriptPath in groupPolicy.GetLogonScriptPaths(userSid))
            {
                if (!string.IsNullOrEmpty(scriptPath))
                    SecurityScanner.AddAutorunPath(ctx.Autorun, scriptPath, excludedSids, StartupSecurityCategory.LogonScript);
            }
        }
        catch (Exception ex)
        {
            log.Error($"Failed to scan logon scripts for {userSid}.", ex);
        }
    }

    public void ScanStartupFolder(ScanContext ctx, string folderPath, HashSet<string> excludedSids,
        HashSet<string>? ownerExcluded)
    {
        try
        {
            if (!fileSystem.DirectoryExists(folderPath))
                return;

            try
            {
                var dirSecurity = fileSystem.GetDirectorySecurity(folderPath);
                var folderInsecure = aclCheck.CheckContainerAcl(dirSecurity, folderPath, excludedSids,
                    StartupSecurityCategory.StartupFolder, SecurityScanner.ContainerWriteRightsMask, ctx.Findings, ctx.Seen);
                if (folderInsecure)
                    ctx.InsecureContainers.Add(folderPath);
            }
            catch (Exception ex)
            {
                log.Error($"Failed to read ACL for folder '{folderPath}'.", ex);
            }

            foreach (var filePath in fileSystem.GetFilesInFolder(folderPath))
            {
                try
                {
                    if (SecurityScanner.IsInertStartupFile(filePath))
                        continue;

                    var fileSecurity = fileSystem.GetFileSecurity(filePath);
                    aclCheck.CheckFileInsideLocationAcl(fileSecurity, filePath, excludedSids,
                        StartupSecurityCategory.StartupFolder, ctx.Findings, ctx.Seen);
                }
                catch (Exception ex)
                {
                    log.Error($"Failed to read ACL for file '{filePath}'.", ex);
                }
            }

            autorunChecker.CollectStartupFolderExecutables(folderPath, ctx.Autorun, ownerExcluded, StartupSecurityCategory.StartupFolder);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to check startup folder '{folderPath}'.", ex);
        }
    }

    private static HashSet<string> BuildPerUserExcluded(HashSet<string> adminSids, string? ownerSid,
        string? interactiveUserSid)
    {
        var excluded = new HashSet<string>(adminSids, StringComparer.OrdinalIgnoreCase);
        if (ownerSid != null)
        {
            excluded.Add(ownerSid);
            if (!adminSids.Contains(ownerSid) && interactiveUserSid != null)
                excluded.Add(interactiveUserSid);
        }

        return excluded;
    }

    public static void ApplyProfileExclusion(string path, HashSet<string> adminSids,
        string? interactiveUserSid, Dictionary<string, string> userProfilePaths,
        HashSet<string> excluded)
    {
        foreach (var (profilePath, ownerSid) in userProfilePaths)
        {
            if (path.StartsWith(profilePath, StringComparison.OrdinalIgnoreCase) &&
                path.Length > profilePath.Length &&
                (path[profilePath.Length] == '\\' || path[profilePath.Length] == '/'))
            {
                excluded.Add(ownerSid);
                if (!adminSids.Contains(ownerSid) && interactiveUserSid != null)
                    excluded.Add(interactiveUserSid);
                break;
            }
        }
    }
}
