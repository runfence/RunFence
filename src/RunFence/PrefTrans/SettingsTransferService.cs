using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Core;

namespace RunFence.PrefTrans;

public class SettingsTransferService(
    ILoggingService log,
    IPrefTransLauncher launcher,
    IPathGrantService pathGrantService,
    IInteractiveUserResolver interactiveUserResolver,
    string baseDirectory)
    : ISettingsTransferService
{
    /// <summary>
    /// Base directory used to locate <c>preftrans.exe</c>.
    /// </summary>
    public string BaseDirectory { get; } = baseDirectory;

    /// <summary>
    /// Returns the shared ProgramData temp directory path used for preftrans log files and settings copies.
    /// The directory is not created by this method — callers call <see cref="Directory.CreateDirectory"/> as needed.
    /// </summary>
    public static string GetSharedTempDir() =>
        Path.Combine(PathConstants.ProgramDataDir, "temp");

    public bool ValidatePrefTransExists(out string expectedPath)
    {
        expectedPath = Path.Combine(BaseDirectory, PathConstants.PrefTransExeName);
        return File.Exists(expectedPath);
    }

    public SettingsTransferResult ExportDesktopSettings(string outputFilePath, int timeoutMs = 30_000, Action? pollCallback = null)
    {
        if (!ValidatePrefTransExists(out var prefTransPath))
        {
            var notFoundMsg = $"preftrans.exe not found at: {prefTransPath}";
            log.Error(notFoundMsg);
            return new SettingsTransferResult(false, notFoundMsg);
        }

        var interactiveSid = interactiveUserResolver.GetInteractiveUserSid();
        if (interactiveSid == null)
        {
            var noSessionMsg = "Cannot export desktop settings: no interactive user session is active.";
            log.Error(noSessionMsg);
            return new SettingsTransferResult(false, noSessionMsg);
        }

        return launcher.RunAndWait(prefTransPath, "store", outputFilePath, interactiveSid, timeoutMs, pollCallback);
    }

    public SettingsTransferResult Import(string settingsFilePath, string accountSid,
        int timeoutMs = 60_000, Action? pollCallback = null)
    {
        if (!ValidatePrefTransExists(out var prefTransPath))
        {
            var notFoundMsg = $"preftrans.exe not found at: {prefTransPath}";
            log.Error(notFoundMsg);
            return new SettingsTransferResult(false, notFoundMsg);
        }

        bool dbModified = false;
        try
        {
            var exeDir = Path.GetDirectoryName(prefTransPath)!;
            dbModified = pathGrantService.EnsureAccess(accountSid, exeDir,
                FileSystemRights.ReadAndExecute, confirm: null).DatabaseModified;
            dbModified |= pathGrantService.EnsureAccess(accountSid, settingsFilePath,
                FileSystemRights.Read | FileSystemRights.Synchronize, confirm: null).DatabaseModified;
        }
        catch (OperationCanceledException)
        {
            return new SettingsTransferResult(false, "Permission grant was declined.");
        }
        catch (Exception ex)
        {
            log.Warn($"Permission check failed, proceeding anyway: {ex.Message}");
        }

        if (string.Equals(accountSid, SidResolutionHelper.GetCurrentUserSid(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(accountSid, interactiveUserResolver.GetInteractiveUserSid(), StringComparison.OrdinalIgnoreCase))
        {
            // De-elevated launch: the process runs as the current or interactive user who already has
            // access to settingsFilePath. No temp copy is needed — pass the file directly.
            var launcherResult = launcher.RunAndWait(prefTransPath, "load", settingsFilePath, accountSid, timeoutMs, pollCallback);
            return launcherResult with { DatabaseModified = dbModified };
        }

        // Credentials path: copy to a shared ProgramData location and restrict access to the target
        // user so the process launched with their credentials can read the settings file.
        var sharedTempDir = GetSharedTempDir();
        var tempSettingsPath = Path.Combine(sharedTempDir, $"rfn_import_{Guid.NewGuid():N}.json");

        try
        {
            Directory.CreateDirectory(sharedTempDir);
            var fileSecurity = BuildRestrictedFileSecurity(accountSid);
            using (var dst = FileSystemAclExtensions.Create(new FileInfo(tempSettingsPath), FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, FileOptions.None, fileSecurity))
            using (var src = File.OpenRead(settingsFilePath))
                src.CopyTo(dst);

            var launcherResult = launcher.RunAndWait(prefTransPath, "load", tempSettingsPath, accountSid, timeoutMs, pollCallback);
            return launcherResult with { DatabaseModified = dbModified };
        }
        catch (Exception ex)
        {
            log.Error("Settings import failed", ex);
            return new SettingsTransferResult(false, $"Import failed: {ex.Message}", dbModified);
        }
        finally
        {
            try
            {
                if (File.Exists(tempSettingsPath))
                    File.Delete(tempSettingsPath);
            }
            catch
            {
            } // best-effort cleanup
        }
    }

    private static FileSecurity BuildRestrictedFileSecurity(string accountSid)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);

        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.AddAccessRule(new FileSystemAccessRule(
            admins, FileSystemRights.FullControl, AccessControlType.Allow));

        var currentUser = WindowsIdentity.GetCurrent().User;
        if (currentUser != null)
            security.AddAccessRule(new FileSystemAccessRule(
                currentUser, FileSystemRights.FullControl, AccessControlType.Allow));

        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(accountSid),
            FileSystemRights.Read | FileSystemRights.Synchronize,
            AccessControlType.Allow));

        return security;
    }
}