using System.Security.AccessControl;
using Microsoft.Win32;
using RunFence.Core;

namespace RunFence.SecurityScanner;

/// <summary>
/// Composition adapter implementing <see cref="IScannerDataAccess"/> by delegating to focused
/// data access objects. <see cref="IEnvironmentDataAccess"/> and the registry sub-interfaces
/// are held as focused interfaces so individual scanners can accept the narrower interface they require.
/// </summary>
public class DefaultScannerDataAccess : IScannerDataAccess
{
    private readonly IEnvironmentDataAccess _environment;
    private readonly IAutorunRegistryAccess _autorunRegistry;
    private readonly IWinlogonRegistryAccess _winlogonRegistry;
    private readonly IServiceRegistryAccess _serviceRegistry;
    private readonly IIfeoRegistryAccess _ifeoRegistry;
    private readonly IWindowsComponentRegistryAccess _windowsComponentRegistry;
    private readonly TaskSchedulerDataAccess _taskScheduler;
    private readonly NativePolicyDataAccess _nativePolicy;
    private readonly IGroupPolicyDataAccess _groupPolicy;

    public DefaultScannerDataAccess()
    {
        _nativePolicy = new NativePolicyDataAccess();
        var ntTranslate = new NTTranslateApi(new ConsoleLoggingService());
        _environment = new EnvironmentDataAccess(_nativePolicy, LogError, ntTranslate);
        _autorunRegistry = new AutorunRegistryDataAccess();
        _winlogonRegistry = new WinlogonRegistryDataAccess();
        _serviceRegistry = new ServiceRegistryDataAccess(LogError);
        _ifeoRegistry = new IfeoRegistryDataAccess();
        _windowsComponentRegistry = new WindowsComponentRegistryDataAccess();
        _taskScheduler = new TaskSchedulerDataAccess();
        _groupPolicy = new GroupPolicyDataAccess();
    }

    // IEnvironmentDataAccess
    public string? GetPublicStartupPath() => _environment.GetPublicStartupPath();
    public string? GetCurrentUserStartupPath() => _environment.GetCurrentUserStartupPath();
    public string? GetCurrentUserSid() => _environment.GetCurrentUserSid();
    public string? GetInteractiveUserSid() => _environment.GetInteractiveUserSid();
    public string? GetInteractiveUserProfilePath(string sid) => _environment.GetInteractiveUserProfilePath(sid);
    public HashSet<string> GetAdminMemberSids() => _environment.GetAdminMemberSids();
    public List<(string Sid, string? ProfilePath)> GetAllLocalUserProfiles() => _environment.GetAllLocalUserProfiles();
    public HashSet<string>? TryGetGroupMemberSids(string groupSid) => _environment.TryGetGroupMemberSids(groupSid);
    public Dictionary<string, HashSet<string>?> BulkLookupGroupMemberSids(IReadOnlyList<string> sids) => _environment.BulkLookupGroupMemberSids(sids);
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

    // IAutorunRegistryAccess
    public RegistrySecurity? GetRegistryKeySecurity(RegistryKey hive, string subKeyPath) => _autorunRegistry.GetRegistryKeySecurity(hive, subKeyPath);
    public List<string> GetRegistryAutorunPaths(RegistryKey hive, string subKeyPath) => _autorunRegistry.GetRegistryAutorunPaths(hive, subKeyPath);
    public List<(string SubKeyPath, string DisplayPath)> GetWow6432RunKeyPaths(string? userSid) => _autorunRegistry.GetWow6432RunKeyPaths(userSid);

    // IWinlogonRegistryAccess
    public RegistrySecurity? GetWinlogonRegistryKeySecurity() => _winlogonRegistry.GetWinlogonRegistryKeySecurity();
    public List<string> GetWinlogonExePaths() => _winlogonRegistry.GetWinlogonExePaths();
    public List<AppInitDllEntry> GetAppInitDllEntries() => _winlogonRegistry.GetAppInitDllEntries();

    // IServiceRegistryAccess
    public RegistrySecurity? GetServiceRegistryKeySecurity(string serviceName) => _serviceRegistry.GetServiceRegistryKeySecurity(serviceName);
    public List<ServiceInfo> GetAutoStartServices() => _serviceRegistry.GetAutoStartServices();

    // IIfeoRegistryAccess
    public RegistrySecurity? GetIfeoRegistryKeySecurity() => _ifeoRegistry.GetIfeoRegistryKeySecurity();
    public RegistrySecurity? GetIfeoWow6432RegistryKeySecurity() => _ifeoRegistry.GetIfeoWow6432RegistryKeySecurity();
    public List<string> GetIfeoSubkeyNames() => _ifeoRegistry.GetIfeoSubkeyNames();
    public string? GetIfeoDebuggerPath(string exeName) => _ifeoRegistry.GetIfeoDebuggerPath(exeName);
    public string? GetIfeoVerifierDlls(string exeName) => _ifeoRegistry.GetIfeoVerifierDlls(exeName);

    // IWindowsComponentRegistryAccess
    public List<RegistryDllEntry> GetPrintMonitorEntries() => _windowsComponentRegistry.GetPrintMonitorEntries();
    public List<(RegistrySecurity? Security, List<string> DllPaths)> GetLsaPackageEntries() => _windowsComponentRegistry.GetLsaPackageEntries();
    public List<RegistryDllEntry> GetNetworkProviderEntries() => _windowsComponentRegistry.GetNetworkProviderEntries();

    // ITaskSchedulerDataAccess
    public List<ScheduledTaskInfo> GetTaskSchedulerData() => _taskScheduler.GetTaskSchedulerData();

    // IGroupPolicyDataAccess
    public string GetGpScriptsDir(string userSid) => _groupPolicy.GetGpScriptsDir(userSid);
    public string GetMachineGpScriptsDir() => _groupPolicy.GetMachineGpScriptsDir();
    public string GetMachineGpUserScriptsDir() => _groupPolicy.GetMachineGpUserScriptsDir();
    public List<string> GetMachineGpScriptPaths() => _groupPolicy.GetMachineGpScriptPaths();
    public string GetSharedWrapperScriptsDir() => _groupPolicy.GetSharedWrapperScriptsDir();
    public List<string> GetLogonScriptPaths(string userSid) => _groupPolicy.GetLogonScriptPaths(userSid);

    public void LogError(string message) => Console.Error.WriteLine(message);
}
