using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Launch.Container;

/// <summary>
/// Handles environment variable overriding, VirtualStore access, and shell folder redirects
/// for AppContainer process launches. All methods are best-effort and log warnings on failure.
/// </summary>
public class AppContainerEnvironmentSetup(ILoggingService log, IShortcutComHelper shortcutHelper, IAppContainerSidProvider sidProvider) : IAppContainerEnvironmentSetup
{
    public IntPtr OverrideProfileEnvironment(IntPtr originalEnv, string profileName)
    {
        try
        {
            var vars = NativeEnvironmentBlock.Read(originalEnv);

            var dataPath = AppContainerPaths.GetContainerDataPath(profileName);
            var drive = Path.GetPathRoot(dataPath)?.TrimEnd('\\') ?? "C:";

            vars["TEMP"] = Path.Combine(dataPath, "Temp");
            vars["TMP"] = Path.Combine(dataPath, "Temp");
            vars["APPDATA"] = Path.Combine(dataPath, "Roaming");
            vars["LOCALAPPDATA"] = Path.Combine(dataPath, "Local");
            vars["PROGRAMDATA"] = Path.Combine(dataPath, "ProgramData");
            vars["USERPROFILE"] = dataPath;
            vars["HOMEPATH"] = dataPath;
            vars["HOMEDRIVE"] = drive;

            var newEnv = NativeEnvironmentBlock.Build(vars);
            ProcessLaunchNative.DestroyEnvironmentBlock(originalEnv);
            return newEnv;
        }
        catch
        {
            ProcessLaunchNative.DestroyEnvironmentBlock(originalEnv);
            return IntPtr.Zero;
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
                AppContainerPaths.GetContainerDataPath(containerName),
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
    public void WriteShellFolderRedirects(string containerName)
    {
        try
        {
            var sidStr = sidProvider.GetSidString(containerName);
            var dataPath = AppContainerPaths.GetContainerDataPath(containerName);

            var keyPath = $@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Storage\{sidStr}\User Shell Folders";
            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            if (key == null)
                return;

            key.SetValue("AppData", Path.Combine(dataPath, "Roaming"), RegistryValueKind.ExpandString);
            key.SetValue("Local AppData", Path.Combine(dataPath, "Local"), RegistryValueKind.ExpandString);
            key.SetValue("Cache", Path.Combine(dataPath, "Temp"), RegistryValueKind.ExpandString);
        }
        catch (Exception ex)
        {
            log.Warn($"WriteShellFolderRedirects failed for '{containerName}': {ex.Message}");
        }
    }
}
