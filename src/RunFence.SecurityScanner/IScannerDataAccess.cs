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

public interface IRegistryDataAccess
{
    RegistrySecurity? GetRegistryKeySecurity(RegistryKey hive, string subKeyPath);
    List<string> GetRegistryAutorunPaths(RegistryKey hive, string subKeyPath);
    RegistrySecurity? GetServiceRegistryKeySecurity(string serviceName);
    RegistrySecurity? GetWinlogonRegistryKeySecurity();
    List<string> GetWinlogonExePaths();
    List<AppInitDllEntry> GetAppInitDllEntries();
    List<ServiceInfo> GetAutoStartServices();
    RegistrySecurity? GetIfeoRegistryKeySecurity();
    RegistrySecurity? GetIfeoWow6432RegistryKeySecurity();
    List<string> GetIfeoSubkeyNames();
    string? GetIfeoDebuggerPath(string exeName);
    string? GetIfeoVerifierDlls(string exeName);
    List<RegistryDllEntry> GetPrintMonitorEntries();
    List<(RegistrySecurity? Security, List<string> DllPaths)> GetLsaPackageEntries();
    List<RegistryDllEntry> GetNetworkProviderEntries();
    List<(string SubKeyPath, string DisplayPath)> GetWow6432RunKeyPaths(string? userSid);
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
    IRegistryDataAccess,
    ITaskSchedulerDataAccess,
    IGroupPolicyDataAccess;