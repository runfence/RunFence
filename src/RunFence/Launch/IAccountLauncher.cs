using System.Security;
using RunFence.Account;
using RunFence.Core.Models;

namespace RunFence.Launch;

/// <summary>
/// Core launching operations for a Windows account.
/// Install-related members (<c>InstallPackage</c>, <c>InstallPackages</c>, <c>IsPackageInstalled</c>)
/// are not included because they reference the internal <see cref="InstallablePackage"/> type;
/// those are accessed directly via the concrete <see cref="AccountLauncher"/> type.
/// </summary>
public interface IAccountLauncher
{
    void LaunchCmd(SecureString? password, CredentialEntry credEntry,
        IReadOnlyDictionary<string, string>? sidNames, LaunchFlags flags = default);

    bool LaunchFolderBrowser(SecureString? password, CredentialEntry credEntry, AppSettings settings, IReadOnlyDictionary<string, string>? sidNames,
        LaunchFlags flags = default,
        Func<string, string, bool>? confirm = null);

    void LaunchEnvironmentVariables(SecureString? password, CredentialEntry credEntry,
        IReadOnlyDictionary<string, string>? sidNames, LaunchFlags flags = default);

    string ResolveTerminalExe(string sid);

    void InstallPackage(InstallablePackage package, SecureString? password, CredentialEntry credEntry,
        IReadOnlyDictionary<string, string>? sidNames, LaunchFlags flags = default);

    void InstallPackages(IReadOnlyList<InstallablePackage> packages, SecureString? password, CredentialEntry credEntry,
        IReadOnlyDictionary<string, string>? sidNames, LaunchFlags flags = default);
}