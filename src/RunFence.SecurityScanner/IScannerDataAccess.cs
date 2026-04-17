using System.Security.AccessControl;
using Microsoft.Win32;

namespace RunFence.SecurityScanner;

public interface IEnvironmentDataAccess
{
    string? GetPublicStartupPath();
    string? GetCurrentUserStartupPath();
    string? GetCurrentUserSid();
    string? GetInteractiveUserSid();
    HashSet<string> GetAdminMemberSids();
    List<(string Sid, string? ProfilePath)> GetAllLocalUserProfiles();
    string? GetInteractiveUserProfilePath(string sid);
    HashSet<string>? TryGetGroupMemberSids(string groupSid);
    /// <summary>
    /// Resolves all provided SIDs in bulk using <c>LsaLookupSids</c> and returns local group
    /// member sets keyed by SID. Non-group and domain SIDs map to null (report as findings).
    /// </summary>
    Dictionary<string, HashSet<string>?> BulkLookupGroupMemberSids(IReadOnlyList<string> sids);
    string ResolveDisplayName(string sidString);
    int? GetAccountLockoutThreshold();
    bool? GetAdminAccountLockoutEnabled();
    bool? GetBlankPasswordRestrictionEnabled();
    List<(string ProfileName, bool Enabled)>? GetFirewallProfileStates();
    (bool IsDisabled, bool IsStopped)? GetWindowsFirewallServiceState();
}

public interface IFileSystemDataAccess
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    DirectorySecurity GetDirectorySecurity(string path);
    FileSecurity GetFileSecurity(string path);
    string[] GetFilesInFolder(string folderPath);
    IEnumerable<string> GetDriveRoots();
    string? ResolveShortcutTarget(string lnkPath);
    void LogError(string message);
}

/// <summary>
/// Registry access for autorun keys: HKLM/HKU run keys and Wow6432Node variants.
/// </summary>
public interface IAutorunRegistryAccess
{
    RegistrySecurity? GetRegistryKeySecurity(RegistryKey hive, string subKeyPath);
    List<string> GetRegistryAutorunPaths(RegistryKey hive, string subKeyPath);
    List<(string SubKeyPath, string DisplayPath)> GetWow6432RunKeyPaths(string? userSid);
}

/// <summary>
/// Registry access for Winlogon configuration.
/// </summary>
public interface IWinlogonRegistryAccess
{
    RegistrySecurity? GetWinlogonRegistryKeySecurity();
    List<string> GetWinlogonExePaths();
    List<AppInitDllEntry> GetAppInitDllEntries();
}

/// <summary>
/// Registry access for Windows services.
/// </summary>
public interface IServiceRegistryAccess
{
    RegistrySecurity? GetServiceRegistryKeySecurity(string serviceName);
    List<ServiceInfo> GetAutoStartServices();
}

/// <summary>
/// Registry access for Image File Execution Options (IFEO).
/// </summary>
public interface IIfeoRegistryAccess
{
    RegistrySecurity? GetIfeoRegistryKeySecurity();
    RegistrySecurity? GetIfeoWow6432RegistryKeySecurity();
    List<string> GetIfeoSubkeyNames();
    string? GetIfeoDebuggerPath(string exeName);
    string? GetIfeoVerifierDlls(string exeName);
}

/// <summary>
/// Registry access for Windows component extension points: print monitors, LSA packages, and network providers.
/// </summary>
public interface IWindowsComponentRegistryAccess
{
    List<RegistryDllEntry> GetPrintMonitorEntries();
    List<(RegistrySecurity? Security, List<string> DllPaths)> GetLsaPackageEntries();
    List<RegistryDllEntry> GetNetworkProviderEntries();
}

public interface ITaskSchedulerDataAccess
{
    List<ScheduledTaskInfo> GetTaskSchedulerData();
}

public interface IGroupPolicyDataAccess
{
    string GetGpScriptsDir(string userSid);
    string GetMachineGpScriptsDir();
    string GetMachineGpUserScriptsDir();
    List<string> GetMachineGpScriptPaths();
    string GetSharedWrapperScriptsDir();
    List<string> GetLogonScriptPaths(string userSid);
}

/// <summary>
/// Composed data access interface used by <see cref="SecurityScanner"/> and its sub-scanners.
/// <see cref="DefaultScannerDataAccess"/> implements all focused sub-interfaces.
/// Individual scanners accept only the focused interface(s) they require.
/// </summary>
public interface IScannerDataAccess :
    IEnvironmentDataAccess,
    IFileSystemDataAccess,
    IAutorunRegistryAccess,
    IWinlogonRegistryAccess,
    IServiceRegistryAccess,
    IIfeoRegistryAccess,
    IWindowsComponentRegistryAccess,
    ITaskSchedulerDataAccess,
    IGroupPolicyDataAccess;