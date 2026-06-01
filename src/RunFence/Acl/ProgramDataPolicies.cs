using RunFence.Core;

namespace RunFence.Acl;

public static class ProgramDataPolicies
{
    public static readonly ProgramDataDirectoryPolicy PackageInstallScripts =
        new("PackageInstallScripts", ProgramDataDirectoryAclProfile.TrustedOnly, AllowsDynamicChildren: false);

    public static readonly ProgramDataDirectoryPolicy Scripts =
        new("scripts", ProgramDataDirectoryAclProfile.CurrentProcessUserFullControl, AllowsDynamicChildren: false);

    public static readonly ProgramDataDirectoryPolicy Temp =
        new("temp", ProgramDataDirectoryAclProfile.TrustedOnly, AllowsDynamicChildren: true);

    public static readonly ProgramDataDirectoryPolicy RunFencePrefTransLogs =
        new("PrefTransLogs", ProgramDataDirectoryAclProfile.TrustedOnly, AllowsDynamicChildren: false);

    public static readonly ProgramDataDirectoryPolicy Ac =
        new("AC", ProgramDataDirectoryAclProfile.TrustedOnly, AllowsDynamicChildren: true);

    public static readonly ProgramDataDirectoryPolicy DragBridge =
        new(PathConstants.DragBridgeTempDir, ProgramDataDirectoryAclProfile.TrustedOnly, AllowsDynamicChildren: true);

    public static readonly ProgramDataDirectoryPolicy Icons =
        new("icons", ProgramDataDirectoryAclProfile.PublicReadTrustedWrite, AllowsDynamicChildren: true);

    public static readonly ProgramDataDirectoryPolicy WindowsTerminalShared =
        new("WindowsTerminal", ProgramDataDirectoryAclProfile.SharedExecutableReadExecute, AllowsDynamicChildren: false);

    public static readonly ProgramDataDirectoryPolicy WindowsTerminalCache =
        new("WindowsTerminalCache", ProgramDataDirectoryAclProfile.TrustedOnly, AllowsDynamicChildren: false);

    public static readonly ProgramDataDirectoryPolicy WindowsTerminalDeploymentWork =
        new("WindowsTerminalDeploymentWork", ProgramDataDirectoryAclProfile.TrustedOnly, AllowsDynamicChildren: true);

    public static readonly ProgramDataFilePolicy ContextMenuIcon =
        new("RunFence.ico", ProgramDataFileAclProfile.PublicIconRead);

    public static IReadOnlyList<ProgramDataDirectoryPolicy> Directories { get; } =
    [
        PackageInstallScripts,
        Scripts,
        Temp,
        RunFencePrefTransLogs,
        Ac,
        DragBridge,
        Icons,
        WindowsTerminalShared,
        WindowsTerminalCache,
        WindowsTerminalDeploymentWork
    ];
}
