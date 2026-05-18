using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launching.Environment;

namespace RunFence.Launch.Container;

/// <summary>
/// Handles environment variable overriding, VirtualStore access, and shell folder redirects
/// for AppContainer process launches.
/// </summary>
public class AppContainerEnvironmentSetup(
    ILoggingService log,
    IShortcutComHelper shortcutHelper,
    IAppContainerSidProvider sidProvider,
    IAppContainerPathProvider pathProvider,
    IAppContainerUserRegistryRoot userRegistryRoot)
    : IAppContainerEnvironmentSetup
{
    private readonly IAppContainerPathProvider _pathProvider = pathProvider;
    private readonly RegistryKey _usersRoot = userRegistryRoot.UsersRoot;

    public EnvironmentBlock CreateLaunchEnvironment(
        IntPtr explorerToken,
        AppContainerEntry entry,
        string containerSid,
        string exePath)
    {
        if (!ProcessLaunchNative.CreateEnvironmentBlock(out var originalEnvironment, explorerToken, false)
            || originalEnvironment == IntPtr.Zero)
        {
            return BuildMinimalEnvironmentBlock(entry.Name);
        }

        Dictionary<string, string> baseVariables;
        try
        {
            baseVariables = NativeEnvironmentBlockReader.Read(originalEnvironment);
        }
        catch
        {
            ProcessLaunchNative.DestroyEnvironmentBlock(originalEnvironment);
            throw;
        }

        var localAppData = baseVariables.GetValueOrDefault("LOCALAPPDATA");
        IntPtr overriddenEnvironment = IntPtr.Zero;
        try
        {
            var overrideResult = OverrideProfileEnvironment(
                originalEnvironment,
                entry.Name,
                out overriddenEnvironment);
            if (overrideResult.Status != AppContainerProfileSetupStatus.Succeeded)
            {
                throw new InvalidOperationException(
                    overrideResult.ErrorMessage ?? $"Environment override failed for '{entry.Name}'.");
            }
        }
        finally
        {
            ProcessLaunchNative.DestroyEnvironmentBlock(originalEnvironment);
        }

        if (overriddenEnvironment == IntPtr.Zero)
            throw new InvalidOperationException($"Environment override returned no environment block for '{entry.Name}'.");

        var launchEnvironment = EnvironmentBlock.Own(overriddenEnvironment, Marshal.FreeHGlobal);
        if (localAppData != null)
        {
            TryGrantVirtualStoreAccess(containerSid, localAppData);
            TryCreateVirtualStoreShortcut(exePath, entry.Name, localAppData);
        }

        return launchEnvironment;
    }

    public AppContainerProfileSetupResult OverrideProfileEnvironment(
        IntPtr originalEnv,
        string profileName,
        out IntPtr rewrittenEnvironment)
    {
        rewrittenEnvironment = IntPtr.Zero;
        try
        {
            var vars = NativeEnvironmentBlockReader.Read(originalEnv);

            var dataPath = _pathProvider.GetContainerDataPath(profileName);
            var drive = Path.GetPathRoot(dataPath)?.TrimEnd('\\') ?? "C:";

            vars["TEMP"] = Path.Combine(dataPath, "Temp");
            vars["TMP"] = Path.Combine(dataPath, "Temp");
            vars["APPDATA"] = Path.Combine(dataPath, "Roaming");
            vars["LOCALAPPDATA"] = Path.Combine(dataPath, "Local");
            vars["PROGRAMDATA"] = Path.Combine(dataPath, "ProgramData");
            vars["USERPROFILE"] = dataPath;
            vars["HOMEPATH"] = dataPath;
            vars["HOMEDRIVE"] = drive;

            using var newEnv = EnvironmentBlock.Build(vars);
            rewrittenEnvironment = newEnv.Detach();
            return AppContainerProfileSetupResult.Success(environmentRewritten: true);
        }
        catch (Exception ex)
        {
            return AppContainerProfileSetupResult.Failure(
                AppContainerProfileSetupStatus.EnvironmentRewriteFailed,
                $"OverrideProfileEnvironment failed for '{profileName}': {ex.Message}");
        }
    }

    /// <summary>
    /// Grants the AppContainer SID Modify rights on the user's VirtualStore directory.
    /// Enables UAC kernel-level write virtualization (Program Files → VirtualStore redirect).
    /// </summary>
    public void TryGrantVirtualStoreAccess(string containerSid, string localAppData)
    {
        try
        {
            var containerIdentity = new SecurityIdentifier(containerSid);
            var vStorePath = Path.Combine(localAppData, "VirtualStore");
            Directory.CreateDirectory(vStorePath);

            var dirInfo = new DirectoryInfo(vStorePath);
            var security = dirInfo.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(
                containerIdentity,
                FileSystemRights.Modify | FileSystemRights.Synchronize,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            dirInfo.SetAccessControl(security);
        }
        catch (Exception ex)
        {
            log.Warn($"TryGrantVirtualStoreAccess failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Revokes the AppContainer SID's Modify ACE on the user's VirtualStore directory.
    /// Called during container deletion to clean up the grant made by <see cref="TryGrantVirtualStoreAccess"/>.
    /// </summary>
    public void TryRevokeVirtualStoreAccess(string containerSid, string localAppData)
    {
        try
        {
            var vStorePath = Path.Combine(localAppData, "VirtualStore");
            if (!Directory.Exists(vStorePath))
                return;

            var containerIdentity = new SecurityIdentifier(containerSid);
            var dirInfo = new DirectoryInfo(vStorePath);
            var security = dirInfo.GetAccessControl();

            var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));
            bool removed = false;
            foreach (FileSystemAccessRule rule in rules)
            {
                if (rule.AccessControlType == AccessControlType.Allow &&
                    rule.IdentityReference is SecurityIdentifier ruleSid &&
                    ruleSid.Equals(containerIdentity))
                {
                    security.RemoveAccessRuleSpecific(rule);
                    removed = true;
                }
            }

            if (removed)
                dirInfo.SetAccessControl(security);
        }
        catch (Exception ex)
        {
            log.Warn($"TryRevokeVirtualStoreAccess failed for SID '{containerSid}': {ex.Message}");
        }
    }

    /// <summary>
    /// Creates a .lnk shortcut in the container's data folder pointing to the VirtualStore
    /// directory for the exe's installation folder. Windows UAC write virtualization always
    /// redirects writes from a protected path to:
    ///   {LOCALAPPDATA}\VirtualStore\{path-without-drive-letter}
    /// so the target is computed directly from the interactive user's actual LOCALAPPDATA
    /// (from the system env block) and the exe's parent directory.
    /// Shortcut is named after the installation directory — all exes in the same folder
    /// share the same VirtualStore path and produce the same shortcut (idempotent overwrite).
    /// </summary>
    public void TryCreateVirtualStoreShortcut(string exePath, string containerName, string localAppData)
    {
        try
        {
            var exeDir = Path.GetDirectoryName(Path.GetFullPath(exePath));
            if (exeDir == null)
                return;

            var root = Path.GetPathRoot(exeDir) ?? "";
            var relativePath = exeDir.Length > root.Length ? exeDir[root.Length..] : null;
            if (string.IsNullOrEmpty(relativePath))
                return;

            var virtualStorePath = Path.Combine(localAppData, "VirtualStore", relativePath);
            var dirName = Path.GetFileName(exeDir);
            var shortcutPath = Path.Combine(
                _pathProvider.GetContainerDataPath(containerName),
                $"{dirName} - VirtualStore.lnk");

            shortcutHelper.WithShortcut(shortcutPath, lnk =>
            {
                lnk.TargetPath = virtualStorePath;
                lnk.Description = $"VirtualStore folder for {dirName} (UAC write virtualization)";
                lnk.Save();
            });
        }
        catch (Exception ex)
        {
            log.Warn($"TryCreateVirtualStoreShortcut failed for '{exePath}': {ex.Message}");
        }
    }

    /// <summary>
    /// Writes shell folder redirects into the AppContainer's registry hive under HKCU,
    /// so SHGetFolderPath / SHGetKnownFolderPath return the container's data paths.
    /// The registry key is indexed by container SID, not profile name.
    /// </summary>
    public AppContainerProfileSetupResult WriteShellFolderRedirects(string containerName, string interactiveUserSid)
    {
        try
        {
            using var interactiveHive = _usersRoot.OpenSubKey(interactiveUserSid, writable: true);
            if (interactiveHive == null)
            {
                return AppContainerProfileSetupResult.Failure(
                    AppContainerProfileSetupStatus.ShellFolderRedirectFailed,
                    $"Interactive user hive '{interactiveUserSid}' is not loaded.");
            }

            var sidStr = sidProvider.GetSidString(containerName);
            var dataPath = _pathProvider.GetContainerDataPath(containerName);

            var keyPath = $@"{interactiveUserSid}\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Storage\{sidStr}\User Shell Folders";
            using RegistryKey key = _usersRoot.CreateSubKey(keyPath)
                ?? throw new InvalidOperationException($"Unable to create registry key '{keyPath}'.");
            key.SetValue("AppData", Path.Combine(dataPath, "Roaming"), RegistryValueKind.ExpandString);
            key.SetValue("Local AppData", Path.Combine(dataPath, "Local"), RegistryValueKind.ExpandString);
            key.SetValue("Cache", Path.Combine(dataPath, "Temp"), RegistryValueKind.ExpandString);
            return AppContainerProfileSetupResult.Success(shellFolderRedirectsWritten: true);
        }
        catch (Exception ex)
        {
            return AppContainerProfileSetupResult.Failure(
                AppContainerProfileSetupStatus.ShellFolderRedirectFailed,
                $"WriteShellFolderRedirects failed for '{containerName}': {ex.Message}");
        }
    }

    private EnvironmentBlock BuildMinimalEnvironmentBlock(string profileName)
    {
        var dataPath = _pathProvider.GetContainerDataPath(profileName);
        var drive = Path.GetPathRoot(dataPath)?.TrimEnd('\\') ?? "C:";
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TEMP"] = Path.Combine(dataPath, "Temp"),
            ["TMP"] = Path.Combine(dataPath, "Temp"),
            ["APPDATA"] = Path.Combine(dataPath, "Roaming"),
            ["LOCALAPPDATA"] = Path.Combine(dataPath, "Local"),
            ["PROGRAMDATA"] = Path.Combine(dataPath, "ProgramData"),
            ["USERPROFILE"] = dataPath,
            ["HOMEPATH"] = dataPath,
            ["HOMEDRIVE"] = drive
        };

        CopyCurrentEnvironmentVariable(vars, "ComSpec");
        CopyCurrentEnvironmentVariable(vars, "OS");
        CopyCurrentEnvironmentVariable(vars, "Path");
        CopyCurrentEnvironmentVariable(vars, "PATHEXT");
        CopyCurrentEnvironmentVariable(vars, "SystemDrive");
        CopyCurrentEnvironmentVariable(vars, "SystemRoot");
        CopyCurrentEnvironmentVariable(vars, "windir");

        using var block = EnvironmentBlock.Build(vars);
        return EnvironmentBlock.Own(block.Detach(), Marshal.FreeHGlobal);
    }

    private static void CopyCurrentEnvironmentVariable(IDictionary<string, string> destination, string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (!string.IsNullOrEmpty(value))
            destination[variableName] = value;
    }
}
