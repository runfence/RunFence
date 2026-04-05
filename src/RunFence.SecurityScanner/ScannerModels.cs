using System.Security.AccessControl;
using RunFence.Core.Models;

namespace RunFence.SecurityScanner;

public record AppInitDllEntry(RegistrySecurity? Security, string DisplayPath, List<string> DllPaths);

public record RegistryDllEntry(string KeyDisplayPath, RegistrySecurity? Security, List<string> DllPaths, string NavigationTarget);

public record ScheduledTaskInfo(
    string TaskPath,
    string FolderPath,
    string TaskName,
    List<string> ExePaths,
    bool IsPerUserTask,
    string? UserSid);

public record ServiceInfo(
    string ServiceName,
    string ImagePath,
    string ExpandedImagePath,
    string? ServiceDllPath);

public record AutorunContext(
    HashSet<string> Paths,
    Dictionary<string, HashSet<string>> PathExcluded,
    HashSet<string> MachineWidePaths,
    Dictionary<string, StartupSecurityCategory> PathCategories);

public record ScanContext(
    HashSet<string> AdminSids,
    List<StartupSecurityFinding> Findings,
    HashSet<(string, string)> Seen,
    AutorunContext Autorun,
    HashSet<string> InsecureContainers,
    HashSet<string> AutorunLocationPaths,
    string? CurrentUserSid = null,
    string? InteractiveUserSid = null);