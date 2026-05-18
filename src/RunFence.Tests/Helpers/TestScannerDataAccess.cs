using System.Security.AccessControl;
using Microsoft.Win32;
using RunFence.Core;
using RunFence.SecurityScanner;

namespace RunFence.Tests.Helpers;

public sealed class TestScannerDataAccess : IScannerDataAccess
{
    private string? _publicStartupPath;
    private string? _currentUserStartupPath;
    private string? _currentUserSid;
    private string? _interactiveUserSid;
    private string? _interactiveProfilePath;
    private bool _interactiveProfilePathSet;
    private HashSet<string> _adminMemberSids = new(StringComparer.OrdinalIgnoreCase);
    private RegistrySecurity? _winlogonKeySecurity;
    private readonly List<string> _winlogonExePaths = [];
    private RegistrySecurity? _ifeoKeySecurity;
    private List<ScheduledTaskInfo>? _taskSchedulerData;
    private string _machineGpScriptsDir = "";
    private readonly List<AppInitDllEntry> _appInitDllEntries = [];
    private readonly List<RegistryDllEntry> _printMonitorEntries = [];
    private readonly List<(RegistrySecurity? Security, List<string> DllPaths)> _lsaPackageEntries = [];
    private readonly List<RegistryDllEntry> _networkProviderEntries = [];
    private string _sharedWrapperScriptsDir = "";
    private readonly Dictionary<string, List<string>> _logonScriptPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IfeoSubkeyInfo> _ifeoSubkeys = [];

    private readonly Dictionary<string, DirectorySecurity> _dirSecurities = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileSecurity> _fileSecurities = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<(string Hive, string Path), RegistrySecurity> _regSecurities =
        new(CaseInsensitiveTupleComparer.Instance);

    private readonly Dictionary<string, string[]> _folderFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirSecurityThrows = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _fileSecurityThrows = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _existingFiles = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<(string Hive, string Path), List<string>> _registryAutorunPaths =
        new(CaseInsensitiveTupleComparer.Instance);

    private readonly Dictionary<string, ShortcutTargetInfo> _shortcutTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RegistrySecurity> _serviceRegSecurities = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RegistrySecurity> _serviceParametersRegSecurities = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Name, string ImagePath, string Expanded, string? Dll)> _services = [];
    private List<(string Sid, string? ProfilePath)>? _allUserProfiles;
    private readonly Dictionary<string, HashSet<string>?> _groupMembers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _gpScriptsDirs = new(StringComparer.OrdinalIgnoreCase);

    // --- Public setters for test configuration ---

    public void SetGroupMemberSids(string groupSid, HashSet<string>? memberSids) =>
        _groupMembers[groupSid] = memberSids;

    public void SetPublicStartupPath(string? path) => _publicStartupPath = path;
    public void SetCurrentUserStartupPath(string? path) => _currentUserStartupPath = path;

    /// <summary>Nulls out startup paths and interactive user SID to isolate non-startup tests.</summary>
    public void ClearStartupPaths()
    {
        _publicStartupPath = null;
        _currentUserStartupPath = null;
        _interactiveUserSid = null;
    }

    public void SetCurrentUserSid(string? sid) => _currentUserSid = sid;
    public void SetInteractiveUserSid(string? sid) => _interactiveUserSid = sid;
    public void SetAdminMemberSids(HashSet<string> sids) => _adminMemberSids = sids;

    public void SetInteractiveProfilePath(string? path)
    {
        _interactiveProfilePath = path;
        _interactiveProfilePathSet = true;
    }

    public void SetTaskSchedulerData(List<ScheduledTaskInfo> data) => _taskSchedulerData = data;
    public void SetMachineGpScriptsDir(string dir) => _machineGpScriptsDir = dir;

    public void AddDirectorySecurity(string path, DirectorySecurity security) =>
        _dirSecurities[path] = security;

    public void AddFileSecurity(string path, FileSecurity security) =>
        _fileSecurities[path] = security;

    public void AddRegistryKeySecurity((string Hive, string Path) key, RegistrySecurity security) =>
        _regSecurities[key] = security;

    public void AddFolderFiles(string folderPath, params string[] files) =>
        _folderFiles[folderPath] = files;

    public void AddFileExists(string path) => _existingFiles.Add(path);

    public void AddRegistryAutorunPaths((string Hive, string Path) key, List<string> paths) =>
        _registryAutorunPaths[key] = paths;

    public void AddShortcutTarget(string lnkPath, string targetPath, string? arguments = null, string? workingDirectory = null) =>
        _shortcutTargets[lnkPath] = new ShortcutTargetInfo(targetPath, arguments, workingDirectory);

    public void AddServiceEntry(string name, string imagePath, string expanded, string? dll = null) =>
        _services.Add((name, imagePath, expanded, dll));

    public void AddServiceRegistryKeySecurity(string name, RegistrySecurity security) =>
        _serviceRegSecurities[name] = security;

    public void AddServiceParametersRegistryKeySecurity(string name, RegistrySecurity security) =>
        _serviceParametersRegSecurities[name] = security;

    public void SetWinlogonRegistryKeySecurity(RegistrySecurity security) =>
        _winlogonKeySecurity = security;

    public void SetIfeoRegistryKeySecurity(RegistrySecurity security) =>
        _ifeoKeySecurity = security;

    public void AddAppInitDllEntry(RegistrySecurity? security, string displayPath, List<string> dllPaths) =>
        _appInitDllEntries.Add(new AppInitDllEntry(security, displayPath, dllPaths));

    public void AddPrintMonitorEntry(string displayPath, RegistrySecurity? security, List<string> dllPaths, string navTarget) =>
        _printMonitorEntries.Add(new RegistryDllEntry(displayPath, security, dllPaths, navTarget));

    public void AddLsaPackageEntry(RegistrySecurity? security, List<string> dllPaths) =>
        _lsaPackageEntries.Add((security, dllPaths));

    public void AddNetworkProviderEntry(string displayPath, RegistrySecurity? security, List<string> dllPaths, string navTarget) =>
        _networkProviderEntries.Add(new RegistryDllEntry(displayPath, security, dllPaths, navTarget));

    public void SetSharedWrapperScriptsDir(string dir) => _sharedWrapperScriptsDir = dir;

    public void SetGpScriptsDir(string userSid, string dir) => _gpScriptsDirs[userSid] = dir;
    public void AddIfeoSubkeyName(string name) =>
        _ifeoSubkeys.Add(new IfeoSubkeyInfo(
            name,
            $@"HKLM\...\Image File Execution Options\{name}",
            $@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{name}",
            null,
            null,
            null));

    public void SetIfeoDebuggerPath(string exeName, string? path) =>
        UpdateIfeoSubkey(exeName, info => info with { DebuggerPath = path });

    public void SetAllUserProfiles(List<(string Sid, string? ProfilePath)> profiles) => _allUserProfiles = profiles;

    private int? _accountLockoutThreshold;
    public void SetAccountLockoutThreshold(int? threshold) => _accountLockoutThreshold = threshold;

    private bool? _adminAccountLockoutEnabled = true;
    public void SetAdminAccountLockoutEnabled(bool? value) => _adminAccountLockoutEnabled = value;

    private bool? _blankPasswordRestrictionEnabled = true;
    public void SetBlankPasswordRestrictionEnabled(bool? value) => _blankPasswordRestrictionEnabled = value;

    private List<(string ProfileName, bool Enabled)>? _firewallProfileStates;

    public void SetFirewallProfileStates(List<(string ProfileName, bool Enabled)>? states) =>
        _firewallProfileStates = states;

    private (bool IsDisabled, bool IsStopped)? _firewallServiceState;

    public void SetWindowsFirewallServiceState((bool IsDisabled, bool IsStopped)? state) =>
        _firewallServiceState = state;

    // --- IScannerDataAccess implementation ---

    public string? GetPublicStartupPath() => _publicStartupPath;
    public string? GetCurrentUserStartupPath() => _currentUserStartupPath;
    public string? GetCurrentUserSid() => _currentUserSid;
    public string? GetInteractiveUserSid() => _interactiveUserSid;
    public HashSet<string> GetAdminMemberSids() => _adminMemberSids;

    public string? GetInteractiveUserProfilePath(string sid)
    {
        if (_interactiveProfilePathSet)
            return _interactiveProfilePath;
        return null;
    }

    public List<(string Sid, string? ProfilePath)> GetAllLocalUserProfiles() => _allUserProfiles ?? [];

    public HashSet<string>? TryGetGroupMemberSids(string groupSid) =>
        _groupMembers.GetValueOrDefault(groupSid);

    public Dictionary<string, HashSet<string>?> BulkLookupGroupMemberSids(IReadOnlyList<string> sids)
    {
        var result = new Dictionary<string, HashSet<string>?>(StringComparer.OrdinalIgnoreCase);
        foreach (var sid in sids)
            result[sid] = _groupMembers.GetValueOrDefault(sid);
        return result;
    }

    public List<ScheduledTaskInfo> GetTaskSchedulerData() => _taskSchedulerData ?? [];

    public List<string> GetLogonScriptPaths(string userSid) =>
        _logonScriptPaths.TryGetValue(userSid, out var paths) ? paths : [];

    public string GetGpScriptsDir(string userSid) =>
        _gpScriptsDirs.GetValueOrDefault(userSid, "");

    public string GetMachineGpScriptsDir() => _machineGpScriptsDir;
    public List<string> GetMachineGpScriptPaths() => [];
    public string GetSharedWrapperScriptsDir() => _sharedWrapperScriptsDir;

    public List<(string SubKeyPath, string DisplayPath)> GetWow6432RunKeyPaths(string? userSid)
    {
        // Default: same logic as DefaultScannerDataAccess
        var paths = new List<(string, string)>
        {
            (@"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Run", @"HKLM\...\Wow6432Node\Run"),
            (@"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\RunOnce", @"HKLM\...\Wow6432Node\RunOnce")
        };
        if (userSid != null)
        {
            paths.Add(($@"{userSid}\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Run", $@"HKU\{userSid}\...\Wow6432Node\Run"));
            paths.Add(($@"{userSid}\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\RunOnce", $@"HKU\{userSid}\...\Wow6432Node\RunOnce"));
        }

        return paths;
    }

    public List<IfeoSubkeyInfo> GetIfeoSubkeys() => _ifeoSubkeys.ToList();

    public void SetIfeoVerifierDlls(string exeName, string? dlls) =>
        UpdateIfeoSubkey(exeName, info => info with { VerifierDlls = dlls });

    public void SetIfeoSubkeySecurity(string exeName, RegistrySecurity security) =>
        UpdateIfeoSubkey(exeName, info => info with { Security = security });

    public void AddIfeoWow6432Subkey(string exeName, string? debuggerPath = null, string? verifierDlls = null, RegistrySecurity? security = null)
    {
        _ifeoSubkeys.Add(new IfeoSubkeyInfo(
            exeName,
            $@"HKLM\...\Wow6432Node\Image File Execution Options\{exeName}",
            $@"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{exeName}",
            security,
            debuggerPath,
            verifierDlls));
    }

    public List<AppInitDllEntry> GetAppInitDllEntries() =>
        _appInitDllEntries.Count > 0 ? _appInitDllEntries : [];

    public List<RegistryDllEntry> GetPrintMonitorEntries() =>
        _printMonitorEntries.Count > 0 ? _printMonitorEntries : [];

    public List<(RegistrySecurity? Security, List<string> DllPaths)> GetLsaPackageEntries() =>
        _lsaPackageEntries.Count > 0 ? _lsaPackageEntries : [];

    public List<RegistryDllEntry> GetNetworkProviderEntries() =>
        _networkProviderEntries.Count > 0 ? _networkProviderEntries : [];

    public string GetMachineGpUserScriptsDir() => "";

    public List<ServiceInfo> GetAutoStartServices() =>
        _services.Select(s => new ServiceInfo(
            s.Name,
            s.ImagePath,
            s.Expanded,
            s.Dll,
            _serviceRegSecurities.GetValueOrDefault(s.Name),
            _serviceParametersRegSecurities.GetValueOrDefault(s.Name))).ToList();

    public RegistrySecurity? GetWinlogonRegistryKeySecurity() => _winlogonKeySecurity;
    public List<string> GetWinlogonExePaths() => _winlogonExePaths;
    public RegistrySecurity? GetIfeoRegistryKeySecurity() => _ifeoKeySecurity;
    private RegistrySecurity? _ifeoWow6432KeySecurity;
    public void SetIfeoWow6432RegistryKeySecurity(RegistrySecurity security) => _ifeoWow6432KeySecurity = security;
    public RegistrySecurity? GetIfeoWow6432RegistryKeySecurity() => _ifeoWow6432KeySecurity;

    public bool DirectoryExists(string path) =>
        _dirSecurities.ContainsKey(path) || _folderFiles.ContainsKey(path) || _dirSecurityThrows.Contains(path);

    public bool FileExists(string path) => _existingFiles.Contains(path);

    public DirectorySecurity GetDirectorySecurity(string path)
    {
        if (_dirSecurityThrows.Contains(path))
            throw new UnauthorizedAccessException("Access denied (test)");
        if (_dirSecurities.TryGetValue(path, out var sec))
            return sec;
        throw new DirectoryNotFoundException($"Not found: {path}");
    }

    public FileSecurity GetFileSecurity(string path)
    {
        if (_fileSecurityThrows.Contains(path))
            throw new UnauthorizedAccessException("Access denied (test)");
        if (_fileSecurities.TryGetValue(path, out var sec))
            return sec;
        throw new FileNotFoundException($"Not found: {path}");
    }

    public RegistrySecurity? GetRegistryKeySecurity(RegistryKey hive, string subKeyPath)
    {
        var hiveName = GetHiveName(hive);
        var key = (hiveName, subKeyPath);
        return _regSecurities.GetValueOrDefault(key);
    }

    public string[] GetFilesInFolder(string folderPath)
    {
        if (_folderFiles.TryGetValue(folderPath, out var files))
            return files;
        return [];
    }

    public IEnumerable<string> GetDriveRoots() => _driveRoots;

    private readonly List<string> _driveRoots = [];
    public void AddDriveRoot(string root) => _driveRoots.Add(root);

    public List<string> GetRegistryAutorunPaths(RegistryKey hive, string subKeyPath)
    {
        var hiveName = GetHiveName(hive);
        var key = (hiveName, subKeyPath);
        if (_registryAutorunPaths.TryGetValue(key, out var paths))
            return paths;
        return [];
    }

    public ShortcutTargetInfo? ResolveShortcutTarget(string lnkPath)
    {
        return _shortcutTargets.GetValueOrDefault(lnkPath);
    }

    public string ResolveDisplayName(string sidString) => sidString;

    public int? GetAccountLockoutThreshold() => _accountLockoutThreshold;
    public bool? GetAdminAccountLockoutEnabled() => _adminAccountLockoutEnabled;
    public bool? GetBlankPasswordRestrictionEnabled() => _blankPasswordRestrictionEnabled;
    public List<(string ProfileName, bool Enabled)>? GetFirewallProfileStates() => _firewallProfileStates;
    public (bool IsDisabled, bool IsStopped)? GetWindowsFirewallServiceState() => _firewallServiceState;

    public void LogError(string message)
    {
        /* suppress in tests */
    }

    public void AddDirSecurityThrows(string path) => _dirSecurityThrows.Add(path);

    private static string GetHiveName(RegistryKey hive)
    {
        if (hive == Registry.LocalMachine)
            return "HKLM";
        if (hive == Registry.CurrentUser)
            return "HKCU";
        if (hive == Registry.Users)
            return "HKU";
        return hive.Name;
    }

    private void UpdateIfeoSubkey(string exeName, Func<IfeoSubkeyInfo, IfeoSubkeyInfo> updater)
    {
        for (int i = 0; i < _ifeoSubkeys.Count; i++)
        {
            if (!_ifeoSubkeys[i].ExeName.Equals(exeName, StringComparison.OrdinalIgnoreCase))
                continue;

            _ifeoSubkeys[i] = updater(_ifeoSubkeys[i]);
            return;
        }

        AddIfeoSubkeyName(exeName);
        UpdateIfeoSubkey(exeName, updater);
    }
}
