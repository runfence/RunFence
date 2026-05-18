namespace RunFence.Account;

/// <summary>
/// Describes a package that can be installed for a user account.
/// </summary>
/// <param name="DisplayName">Label shown in the context menu and install list.</param>
/// <param name="PowerShellCommand">Full PowerShell statement(s) executed from the generated package-install PowerShell script.</param>
/// <param name="DetectExeName">If set, checks LocalAppData\Microsoft\WindowsApps\{DetectExeName} to determine if installed (MSIX packages).</param>
/// <param name="DetectProfileRelativePath">If set, checks {UserProfile}\{DetectProfileRelativePath} to determine if installed.</param>
/// <param name="RequiredPackages">Packages that must be installed before this package.</param>
public sealed record InstallablePackage(
    string DisplayName,
    string PowerShellCommand,
    string? DetectExeName = null,
    string? DetectProfileRelativePath = null,
    IReadOnlyList<InstallablePackage>? RequiredPackages = null);
