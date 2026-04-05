using System.Security.AccessControl;
using Microsoft.Win32;

namespace RunFence.SecurityScanner;

public class DefaultScannerDataAccess : IScannerDataAccess
{
    private readonly EnvironmentDataAccess _environment;
    private readonly RegistryDataAccess _registry;
    private readonly SystemRegistryDataAccess _systemRegistry;
    private readonly TaskSchedulerDataAccess _taskScheduler;
    private readonly NativePolicyDataAccess _nativePolicy;
    private readonly GroupPolicyDataAccess _groupPolicy;

    public DefaultScannerDataAccess()
    {
        _nativePolicy = new NativePolicyDataAccess();
        _environment = new EnvironmentDataAccess(_nativePolicy, LogError);
        _registry = new RegistryDataAccess();
        _systemRegistry = new SystemRegistryDataAccess(LogError);
        _taskScheduler = new TaskSchedulerDataAccess();
        _groupPolicy = new GroupPolicyDataAccess();
    }

    // IEnvironmentDataAccess — delegated to EnvironmentDataAccess
    public string? GetPublicStartupPath() => _environment.GetPublicStartupPath();
    public string? GetCurrentUserStartupPath() => _environment.GetCurrentUserStartupPath();
    public string? GetCurrentUserSid() => _environment.GetCurrentUserSid();
    public string? GetInteractiveUserSid() => _environment.GetInteractiveUserSid();
    public string? GetInteractiveUserProfilePath(string sid) => _environment.GetInteractiveUserProfilePath(sid);
    public HashSet<string> GetAdminMemberSids() => _environment.GetAdminMemberSids();
    public List<(string Sid, string? ProfilePath)> GetAllLocalUserProfiles() => _environment.GetAllLocalUserProfiles();
    public HashSet<string>? TryGetGroupMemberSids(string groupSid) => _environment.TryGetGroupMemberSids(groupSid);
    public string ResolveDisplayName(string sidString) => _environment.ResolveDisplayName(sidString);
    public List<(string ProfileName, bool Enabled)>? GetFirewallProfileStates() => _environment.GetFirewallProfileStates();
    public int? GetAccountLockoutThreshold() => _environment.GetAccountLockoutThreshold();
    public (bool IsDisabled, bool IsStopped)? GetWindowsFirewallServiceState() => _environment.GetWindowsFirewallServiceState();
    public bool? GetAdminAccountLockoutEnabled() => _environment.GetAdminAccountLockoutEnabled();
    public bool? GetBlankPasswordRestrictionEnabled() => _environment.GetBlankPasswordRestrictionEnabled();

    // IFileSystemDataAccess
    public IEnumerable<string> GetDriveRoots() => _nativePolicy.GetDriveRoots();
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool FileExists(string path) => File.Exists(path);
    public DirectorySecurity GetDirectorySecurity(string path) => new DirectoryInfo(path).GetAccessControl();
    public FileSecurity GetFileSecurity(string path) => new FileInfo(path).GetAccessControl();
    public string[] GetFilesInFolder(string folderPath) => Directory.GetFiles(folderPath);
    public string? ResolveShortcutTarget(string lnkPath) => _taskScheduler.ResolveShortcutTarget(lnkPath);

    // IRegistryDataAccess — delegated to RegistryDataAccess / SystemRegistryDataAccess
    public RegistrySecurity? GetRegistryKeySecurity(RegistryKey hive, string subKeyPath) =>
        _registry.GetRegistryKeySecurity(hive, subKeyPath);

    public List<string> GetRegistryAutorunPaths(RegistryKey hive, string subKeyPath) =>
        _registry.GetRegistryAutorunPaths(hive, subKeyPath);

    public List<(string SubKeyPath, string DisplayPath)> GetWow6432RunKeyPaths(string? userSid) =>
        _registry.GetWow6432RunKeyPaths(userSid);

    public RegistrySecurity? GetServiceRegistryKeySecurity(string serviceName) =>
        _systemRegistry.GetServiceRegistryKeySecurity(serviceName);

    public RegistrySecurity? GetWinlogonRegistryKeySecurity() =>
        _systemRegistry.GetWinlogonRegistryKeySecurity();

    public List<string> GetWinlogonExePaths() =>
        _systemRegistry.GetWinlogonExePaths();

    public List<AppInitDllEntry> GetAppInitDllEntries() =>
        _systemRegistry.GetAppInitDllEntries();

    public List<ServiceInfo> GetAutoStartServices() =>
        _systemRegistry.GetAutoStartServices();

    public RegistrySecurity? GetIfeoRegistryKeySecurity() =>
        _systemRegistry.GetIfeoRegistryKeySecurity();

    public RegistrySecurity? GetIfeoWow6432RegistryKeySecurity() =>
        _systemRegistry.GetIfeoWow6432RegistryKeySecurity();

    public List<string> GetIfeoSubkeyNames() =>
        _systemRegistry.GetIfeoSubkeyNames();

    public string? GetIfeoDebuggerPath(string exeName) =>
        _systemRegistry.GetIfeoDebuggerPath(exeName);

    public string? GetIfeoVerifierDlls(string exeName) =>
        _systemRegistry.GetIfeoVerifierDlls(exeName);

    public List<RegistryDllEntry> GetPrintMonitorEntries() =>
        _systemRegistry.GetPrintMonitorEntries();

    public List<(RegistrySecurity? Security, List<string> DllPaths)> GetLsaPackageEntries() =>
        _systemRegistry.GetLsaPackageEntries();

    public List<RegistryDllEntry> GetNetworkProviderEntries() =>
        _systemRegistry.GetNetworkProviderEntries();

    // ITaskSchedulerDataAccess — delegated to TaskSchedulerDataAccess
    public List<ScheduledTaskInfo> GetTaskSchedulerData() =>
        _taskScheduler.GetTaskSchedulerData();

    // IGroupPolicyDataAccess — delegated to GroupPolicyDataAccess
    public string GetGpScriptsDir(string userSid) => _groupPolicy.GetGpScriptsDir(userSid);
    public string GetMachineGpScriptsDir() => _groupPolicy.GetMachineGpScriptsDir();
    public string GetMachineGpUserScriptsDir() => _groupPolicy.GetMachineGpUserScriptsDir();
    public List<string> GetMachineGpScriptPaths() => _groupPolicy.GetMachineGpScriptPaths();
    public string GetSharedWrapperScriptsDir() => _groupPolicy.GetSharedWrapperScriptsDir();
    public List<string> GetLogonScriptPaths(string userSid) => _groupPolicy.GetLogonScriptPaths(userSid);

    public void LogError(string message) => Console.Error.WriteLine(message);
}