using System.Security.AccessControl;
using RunFence.Core.Models;

namespace RunFence.SecurityScanner;

public record AppInitDllEntry(RegistrySecurity? Security, string DisplayPath, List<string> DllPaths);

public record RegistryDllEntry(string KeyDisplayPath, RegistrySecurity? Security, List<string> DllPaths, string NavigationTarget);

public record ShortcutTargetInfo(string? TargetPath, string? Arguments, string? WorkingDirectory);

public record ScheduledTaskActionInfo(
    int ActionType,
    string? Path,
    string? Arguments,
    string? WorkingDirectory);

public record ScheduledTaskInfo(
    string TaskPath,
    List<ScheduledTaskActionInfo> Actions,
    string? PrincipalSid,
    string? TaskSecurityDescriptor,
    bool IsPerUserTask,
    string? UserSid);

public record ServiceInfo(
    string ServiceName,
    string ImagePath,
    string ExpandedImagePath,
    string? ServiceDllPath,
    RegistrySecurity? ServiceKeySecurity,
    RegistrySecurity? ParametersKeySecurity);

public record IfeoSubkeyInfo(
    string ExeName,
    string DisplayPath,
    string NavigationTarget,
    RegistrySecurity? Security,
    string? DebuggerPath,
    string? VerifierDlls);

public record AutorunCommandContext(
    string SourceDescription,
    string? Arguments,
    string? WorkingDirectory,
    string? NavigationTarget = null);

public record AutorunWarning(
    StartupSecurityCategory Category,
    string TargetDescription,
    string AffectedScope,
    string Message,
    string? NavigationTarget = null);

public record AutorunContext(
    HashSet<string> Paths,
    Dictionary<string, HashSet<string>> PathExcluded,
    HashSet<string> MachineWidePaths,
    Dictionary<string, StartupSecurityCategory> PathCategories,
    Dictionary<string, List<AutorunCommandContext>> PathCommandContexts,
    List<AutorunWarning> PendingWarnings);

public record ScanContext(
    HashSet<string> AdminSids,
    List<StartupSecurityFinding> Findings,
    HashSet<(string, string)> Seen,
    AutorunContext Autorun,
    HashSet<string> InsecureContainers,
    HashSet<string> AutorunLocationPaths,
    string? CurrentUserSid = null,
    string? InteractiveUserSid = null);
