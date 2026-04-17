using System.Security.AccessControl;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.SecurityScanner;

public class SecurityScanner
{
    // Containers (startup folders, logon script dirs, shared wrapper scripts dir).
    // DeleteSubdirectoriesAndFiles intentionally excluded: deleting from a container without
    // WriteData cannot cause privilege escalation — no new executable can be placed.
    public const FileSystemRights ContainerWriteRightsMask =
        FileSystemRights.WriteData | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    // Files inside autorun locations and external autorun executables/DLLs.
    public const FileSystemRights TargetFileWriteRightsMask =
        FileSystemRights.WriteData | FileSystemRights.AppendData |
        FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    // Disk root ACL check: creation, deletion, permission, and ownership changes.
    public const FileSystemRights DiskRootCheckRightsMask =
        FileSystemRights.WriteData | FileSystemRights.AppendData |
        FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership |
        FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles;

    // Run/RunOnce, Winlogon, AppInit_DLLs registry keys
    public const RegistryRights RunRegistryWriteRightsMask =
        RegistryRights.SetValue | RegistryRights.ChangePermissions | RegistryRights.TakeOwnership;

    // Service registry keys (Services\*, Lsa, Print Monitors, Network Providers)
    public const RegistryRights ServiceRegistryWriteRightsMask =
        RegistryRights.SetValue | RegistryRights.ChangePermissions | RegistryRights.TakeOwnership;

    // Denylist: files that are never executable via ShellExecute in startup folders.
    // Everything NOT in this list is checked — fails open for unknown/new extensions.
    private static readonly HashSet<string> InertStartupFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "desktop.ini", "thumbs.db",
    };

    private static readonly HashSet<string> InertExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".ini", ".cfg", ".xml", ".json", ".dat", ".db", ".tmp", ".bak", ".old",
        ".bmp", ".jpg", ".jpeg", ".png", ".gif", ".ico", ".tif", ".tiff", ".svg", ".webp",
    };

    public static bool IsInertStartupFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (InertStartupFileNames.Contains(fileName))
            return true;
        var ext = Path.GetExtension(filePath);
        return InertExtensions.Contains(ext);
    }

    // FileSystemRights enum aliases: WriteData=CreateFiles (0x2), AppendData=CreateDirectories (0x4).
    // .ToString() picks the "Create*" names which are correct for folders but wrong for files.
    private static readonly (FileSystemRights Flag, string FileName, string DirName)[] s_fileSystemRightsLabels =
    [
        (FileSystemRights.WriteData, "WriteData", "CreateFiles"),
        (FileSystemRights.AppendData, "AppendData", "CreateDirectories"),
        (FileSystemRights.ChangePermissions, "ChangePermissions", "ChangePermissions"),
        (FileSystemRights.TakeOwnership, "TakeOwnership", "TakeOwnership"),
        (FileSystemRights.Delete, "Delete", "Delete"),
        (FileSystemRights.DeleteSubdirectoriesAndFiles, "DeleteSubdirectoriesAndFiles", "DeleteSubdirectoriesAndFiles"),
    ];

    private readonly IScannerDataAccess _dataAccess;
    private readonly AclCheckHelper _aclCheck;
    private readonly AutorunChecker _autorunChecker;
    private readonly PerUserScanner _perUserScanner;
    private readonly MachineLevelRegistryScanner _registryScanner;
    private readonly MachineLevelServiceScanner _serviceScanner;
    private readonly DiskRootScanner _diskRootScanner;

    public SecurityScanner() : this(new DefaultScannerDataAccess())
    {
    }

    public SecurityScanner(IScannerDataAccess dataAccess)
    {
        _dataAccess = dataAccess;
        _aclCheck = new AclCheckHelper(dataAccess, dataAccess, dataAccess);
        _autorunChecker = new AutorunChecker(dataAccess, _aclCheck);
        _perUserScanner = new PerUserScanner(dataAccess, _aclCheck, _autorunChecker);
        _registryScanner = new MachineLevelRegistryScanner(dataAccess, dataAccess, dataAccess, dataAccess, dataAccess, _aclCheck);
        _serviceScanner = new MachineLevelServiceScanner(dataAccess, _aclCheck, _perUserScanner);
        _diskRootScanner = new DiskRootScanner(dataAccess, _aclCheck);
    }

    public static string FormatFileSystemRights(FileSystemRights rights, bool isDirectory)
    {
        var parts = new List<string>();
        foreach (var (flag, fileName, dirName) in s_fileSystemRightsLabels)
        {
            if ((rights & flag) != 0)
                parts.Add(isDirectory ? dirName : fileName);
        }

        return parts.Count > 0 ? string.Join(", ", parts) : rights.ToString();
    }

    public static string ExpandEnvVars(string value)
    {
        if (!value.Contains('%'))
            return value;
        try
        {
            return Environment.ExpandEnvironmentVariables(value);
        }
        catch
        {
            return value;
        }
    }

    public static void AddAutorunPath(AutorunContext autorun, string path, HashSet<string>? ownerExcluded,
        StartupSecurityCategory category)
    {
        autorun.Paths.Add(path);
        autorun.PathCategories.TryAdd(path, category);
        if (ownerExcluded == null)
        {
            autorun.MachineWidePaths.Add(path);
            autorun.PathExcluded.Remove(path);
            return;
        }

        if (autorun.MachineWidePaths.Contains(path))
            return;
        if (!autorun.PathExcluded.TryGetValue(path, out var existing))
            autorun.PathExcluded[path] = new HashSet<string>(ownerExcluded, StringComparer.OrdinalIgnoreCase);
        else
            existing.IntersectWith(ownerExcluded);
    }

    public List<StartupSecurityFinding> RunChecks(CancellationToken ct = default)
    {
        var findings = new List<StartupSecurityFinding>();
        var seen = new HashSet<(string, string)>(CaseInsensitiveTupleComparer.Instance);
        var insecureContainers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var autorunLocationPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var autorun = new AutorunContext(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, StartupSecurityCategory>(StringComparer.OrdinalIgnoreCase));

        var currentUserSid = _dataAccess.GetCurrentUserSid();
        var interactiveUserSid = _dataAccess.GetInteractiveUserSid();
        var adminSids = _dataAccess.GetAdminMemberSids();
        // Pre-warm member caches for commonly-encountered builtin groups in parallel.
        // S-1-5-32-544 is already cached inside GetAdminMemberSids(); warm Users and Guests
        // so their first ACL check hits the cache rather than a synchronous AD lookup.
        _aclCheck.StartPrewarmGroupMembers("S-1-5-32-545", "S-1-5-32-546");

        var allProfiles = _dataAccess.GetAllLocalUserProfiles();
        var userProfilePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (sid, profilePath) in allProfiles)
        {
            if (!string.IsNullOrEmpty(profilePath))
                userProfilePaths[profilePath] = sid;
        }

        var ctx = new ScanContext(adminSids, findings, seen, autorun, insecureContainers, autorunLocationPaths,
            CurrentUserSid: currentUserSid, InteractiveUserSid: interactiveUserSid);

        ct.ThrowIfCancellationRequested();
        _serviceScanner.ScanPublicStartupFolder(ctx);
        ct.ThrowIfCancellationRequested();
        _registryScanner.ScanMachineRegistryRunKeys(ctx);
        ct.ThrowIfCancellationRequested();
        _registryScanner.ScanWinlogon(ctx);
        ct.ThrowIfCancellationRequested();
        _registryScanner.ScanAppInitDlls(ctx);
        ct.ThrowIfCancellationRequested();
        _serviceScanner.ScanTaskScheduler(ctx);
        ct.ThrowIfCancellationRequested();
        _serviceScanner.ScanMachineGpScripts(ctx);
        ct.ThrowIfCancellationRequested();
        _serviceScanner.ScanSharedWrapperScripts(ctx);
        ct.ThrowIfCancellationRequested();
        _serviceScanner.ScanServices(ctx);
        ct.ThrowIfCancellationRequested();
        _registryScanner.ScanIfeo(ctx);
        ct.ThrowIfCancellationRequested();
        _registryScanner.ScanSystemDllLocations(ctx);
        ct.ThrowIfCancellationRequested();
        _serviceScanner.ScanAccountLockoutPolicy(ctx);
        ct.ThrowIfCancellationRequested();
        _serviceScanner.ScanBlankPasswordPolicy(ctx);
        ct.ThrowIfCancellationRequested();
        _serviceScanner.ScanWindowsFirewall(ctx);
        ct.ThrowIfCancellationRequested();
        _diskRootScanner.ScanDiskRoots(ctx);
        ct.ThrowIfCancellationRequested();
        _perUserScanner.ScanPerUserLocations(ctx, allProfiles, ct);
        ct.ThrowIfCancellationRequested();
        _autorunChecker.CheckAutorunExecutables(ctx, userProfilePaths);

        return findings;
    }
}