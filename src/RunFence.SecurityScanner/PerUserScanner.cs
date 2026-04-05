using Microsoft.Win32;
using RunFence.Core.Models;

namespace RunFence.SecurityScanner;

public class PerUserScanner
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOnceKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";

    private readonly IScannerDataAccess _dataAccess;
    private readonly AclCheckHelper _aclCheck;
    private readonly AutorunChecker _autorunChecker;

    public PerUserScanner(IScannerDataAccess dataAccess, AclCheckHelper aclCheck, AutorunChecker autorunChecker)
    {
        _dataAccess = dataAccess;
        _aclCheck = aclCheck;
        _autorunChecker = autorunChecker;
    }

    public void ScanPerUserLocations(ScanContext ctx,
        List<(string Sid, string? ProfilePath)> allProfiles,
        CancellationToken ct)
    {
        bool skipInteractive = ctx.InteractiveUserSid != null &&
                               string.Equals(ctx.InteractiveUserSid, ctx.CurrentUserSid, StringComparison.OrdinalIgnoreCase);

        // Current user startup folder
        var currentUserStartup = _dataAccess.GetCurrentUserStartupPath();
        if (!string.IsNullOrEmpty(currentUserStartup))
        {
            var excludedForCurrentUser = BuildPerUserExcluded(ctx.AdminSids, ctx.CurrentUserSid, ctx.CurrentUserSid, ctx.InteractiveUserSid);
            ctx.AutorunLocationPaths.Add(currentUserStartup);
            ScanStartupFolder(currentUserStartup, excludedForCurrentUser, excludedForCurrentUser, ctx.Findings, ctx.Seen, ctx.InsecureContainers, ctx.Autorun);
        }

        // Current user logon scripts
        if (ctx.CurrentUserSid != null)
        {
            var excludedForCurrentUser = BuildPerUserExcluded(ctx.AdminSids, ctx.CurrentUserSid, ctx.CurrentUserSid, ctx.InteractiveUserSid);
            ScanUserLogonScripts(ctx.CurrentUserSid, excludedForCurrentUser, ctx.Findings, ctx.Seen, ctx.InsecureContainers, ctx.AutorunLocationPaths, ctx.Autorun);
        }

        // Interactive user startup folder and logon scripts
        if (!skipInteractive && ctx.InteractiveUserSid != null)
        {
            var excludedForInteractive = BuildPerUserExcluded(ctx.AdminSids, ctx.InteractiveUserSid, ctx.CurrentUserSid, ctx.InteractiveUserSid);

            var profilePath = _dataAccess.GetInteractiveUserProfilePath(ctx.InteractiveUserSid);
            if (!string.IsNullOrEmpty(profilePath))
            {
                var interactiveStartup = Path.Combine(profilePath,
                    @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup");
                ctx.AutorunLocationPaths.Add(interactiveStartup);
                ScanStartupFolder(interactiveStartup, excludedForInteractive, excludedForInteractive, ctx.Findings, ctx.Seen, ctx.InsecureContainers, ctx.Autorun);
            }

            ScanUserLogonScripts(ctx.InteractiveUserSid, excludedForInteractive, ctx.Findings, ctx.Seen, ctx.InsecureContainers, ctx.AutorunLocationPaths, ctx.Autorun);
        }

        ct.ThrowIfCancellationRequested();

        ScanPerUserRegistryRunKeys(ctx, skipInteractive);

        ct.ThrowIfCancellationRequested();

        ScanAllUserProfiles(ctx, allProfiles, ct);
    }

    private void ScanPerUserRegistryRunKeys(ScanContext ctx, bool skipInteractive)
    {
        var hkcuExcluded = BuildPerUserExcluded(ctx.AdminSids, ctx.CurrentUserSid, ctx.CurrentUserSid, ctx.InteractiveUserSid);
        _aclCheck.CheckRegistryKey(Registry.CurrentUser, RunKeyPath, @"HKCU\...\Run",
            hkcuExcluded, SecurityScanner.RunRegistryWriteRightsMask, StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen, ctx.Autorun, hkcuExcluded,
            @"HKEY_CURRENT_USER\" + RunKeyPath);
        _aclCheck.CheckRegistryKey(Registry.CurrentUser, RunOnceKeyPath, @"HKCU\...\RunOnce",
            hkcuExcluded, SecurityScanner.RunRegistryWriteRightsMask, StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen, ctx.Autorun, hkcuExcluded,
            @"HKEY_CURRENT_USER\" + RunOnceKeyPath);

        foreach (var (path, display) in _dataAccess.GetWow6432RunKeyPaths(ctx.CurrentUserSid))
        {
            if (!path.StartsWith(ctx.CurrentUserSid ?? "---", StringComparison.OrdinalIgnoreCase))
                continue;
            _aclCheck.CheckRegistryKey(Registry.Users, path, display,
                hkcuExcluded, SecurityScanner.RunRegistryWriteRightsMask, StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen, ctx.Autorun, hkcuExcluded,
                @"HKEY_USERS\" + path);
        }

        if (!skipInteractive && ctx.InteractiveUserSid != null)
        {
            var hkuExcluded = BuildPerUserExcluded(ctx.AdminSids, ctx.InteractiveUserSid, ctx.CurrentUserSid, ctx.InteractiveUserSid);
            var hkuRunPath = $@"{ctx.InteractiveUserSid}\{RunKeyPath}";
            var hkuRunOncePath = $@"{ctx.InteractiveUserSid}\{RunOnceKeyPath}";
            _aclCheck.CheckRegistryKey(Registry.Users, hkuRunPath, $@"HKU\{ctx.InteractiveUserSid}\...\Run",
                hkuExcluded, SecurityScanner.RunRegistryWriteRightsMask, StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen, ctx.Autorun, hkuExcluded,
                @"HKEY_USERS\" + hkuRunPath);
            _aclCheck.CheckRegistryKey(Registry.Users, hkuRunOncePath, $@"HKU\{ctx.InteractiveUserSid}\...\RunOnce",
                hkuExcluded, SecurityScanner.RunRegistryWriteRightsMask, StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen, ctx.Autorun, hkuExcluded,
                @"HKEY_USERS\" + hkuRunOncePath);

            foreach (var (path, display) in _dataAccess.GetWow6432RunKeyPaths(ctx.InteractiveUserSid))
            {
                if (!path.StartsWith(ctx.InteractiveUserSid, StringComparison.OrdinalIgnoreCase))
                    continue;
                _aclCheck.CheckRegistryKey(Registry.Users, path, display,
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

                var excluded = BuildPerUserExcluded(ctx.AdminSids, userSid, ctx.CurrentUserSid, ctx.InteractiveUserSid);

                if (!string.IsNullOrEmpty(profilePath))
                {
                    var startupPath = Path.Combine(profilePath,
                        @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup");
                    if (_dataAccess.DirectoryExists(startupPath))
                    {
                        ctx.AutorunLocationPaths.Add(startupPath);
                        ScanStartupFolder(startupPath, excluded, excluded, ctx.Findings, ctx.Seen, ctx.InsecureContainers, ctx.Autorun);
                    }
                }

                try
                {
                    var hkuRunPath = $@"{userSid}\{RunKeyPath}";
                    _aclCheck.CheckRegistryKey(Registry.Users, hkuRunPath, $@"HKU\{userSid}\...\Run",
                        excluded, SecurityScanner.RunRegistryWriteRightsMask, StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen, ctx.Autorun, excluded,
                        @"HKEY_USERS\" + hkuRunPath);
                }
                catch
                {
                    /* HKU hive not loaded */
                }

                try
                {
                    var hkuRunOncePath = $@"{userSid}\{RunOnceKeyPath}";
                    _aclCheck.CheckRegistryKey(Registry.Users, hkuRunOncePath, $@"HKU\{userSid}\...\RunOnce",
                        excluded, SecurityScanner.RunRegistryWriteRightsMask, StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen, ctx.Autorun, excluded,
                        @"HKEY_USERS\" + hkuRunOncePath);
                }
                catch
                {
                    /* HKU hive not loaded */
                }

                foreach (var (path, display) in _dataAccess.GetWow6432RunKeyPaths(userSid))
                {
                    if (!path.StartsWith(userSid, StringComparison.OrdinalIgnoreCase))
                        continue;
                    try
                    {
                        _aclCheck.CheckRegistryKey(Registry.Users, path, display,
                            excluded, SecurityScanner.RunRegistryWriteRightsMask, StartupSecurityCategory.RegistryRunKey, ctx.Findings, ctx.Seen, ctx.Autorun, excluded,
                            @"HKEY_USERS\" + path);
                    }
                    catch
                    {
                        /* HKU hive not loaded */
                    }
                }

                ScanUserLogonScripts(userSid, excluded, ctx.Findings, ctx.Seen, ctx.InsecureContainers, ctx.AutorunLocationPaths, ctx.Autorun);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _dataAccess.LogError($"Failed to enumerate user profiles: {ex.Message}");
        }
    }

    private void ScanUserLogonScripts(string userSid, HashSet<string> excludedSids,
        List<StartupSecurityFinding> findings, HashSet<(string, string)> seen,
        HashSet<string> insecureContainers, HashSet<string> autorunLocationPaths,
        AutorunContext autorun)
    {
        try
        {
            var gpDir = _dataAccess.GetGpScriptsDir(userSid);
            if (!string.IsNullOrEmpty(gpDir) && _dataAccess.DirectoryExists(gpDir))
            {
                autorunLocationPaths.Add(gpDir);
                try
                {
                    var dirSecurity = _dataAccess.GetDirectorySecurity(gpDir);
                    bool insecure = _aclCheck.CheckContainerAcl(dirSecurity, gpDir, excludedSids,
                        StartupSecurityCategory.LogonScript, SecurityScanner.ContainerWriteRightsMask, findings, seen);
                    if (insecure)
                        insecureContainers.Add(gpDir);
                }
                catch (Exception ex)
                {
                    _dataAccess.LogError($"Failed to read ACL for GP scripts dir '{gpDir}': {ex.Message}");
                }
            }

            foreach (var scriptPath in _dataAccess.GetLogonScriptPaths(userSid))
            {
                if (!string.IsNullOrEmpty(scriptPath))
                    SecurityScanner.AddAutorunPath(autorun, scriptPath, excludedSids, StartupSecurityCategory.LogonScript);
            }
        }
        catch (Exception ex)
        {
            _dataAccess.LogError($"Failed to scan logon scripts for {userSid}: {ex.Message}");
        }
    }

    public void ScanStartupFolder(string folderPath, HashSet<string> excludedSids,
        HashSet<string>? ownerExcluded, List<StartupSecurityFinding> findings,
        HashSet<(string, string)> seen, HashSet<string> insecureContainers,
        AutorunContext autorun)
    {
        try
        {
            if (!_dataAccess.DirectoryExists(folderPath))
                return;

            try
            {
                var dirSecurity = _dataAccess.GetDirectorySecurity(folderPath);
                var folderInsecure = _aclCheck.CheckContainerAcl(dirSecurity, folderPath, excludedSids,
                    StartupSecurityCategory.StartupFolder, SecurityScanner.ContainerWriteRightsMask, findings, seen);
                if (folderInsecure)
                    insecureContainers.Add(folderPath);
            }
            catch (Exception ex)
            {
                _dataAccess.LogError($"Failed to read ACL for folder '{folderPath}': {ex.Message}");
            }

            foreach (var filePath in _dataAccess.GetFilesInFolder(folderPath))
            {
                try
                {
                    if (SecurityScanner.IsInertStartupFile(filePath))
                        continue;

                    var fileSecurity = _dataAccess.GetFileSecurity(filePath);
                    _aclCheck.CheckFileInsideLocationAcl(fileSecurity, filePath, excludedSids,
                        StartupSecurityCategory.StartupFolder, findings, seen);
                }
                catch (Exception ex)
                {
                    _dataAccess.LogError($"Failed to read ACL for file '{filePath}': {ex.Message}");
                }
            }

            _autorunChecker.CollectStartupFolderExecutables(folderPath, autorun, ownerExcluded, StartupSecurityCategory.StartupFolder);
        }
        catch (Exception ex)
        {
            _dataAccess.LogError($"Failed to check startup folder '{folderPath}': {ex.Message}");
        }
    }

    private static HashSet<string> BuildPerUserExcluded(HashSet<string> adminSids, string? ownerSid,
        string? currentUserSid, string? interactiveUserSid)
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