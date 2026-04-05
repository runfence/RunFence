using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Launch;

namespace RunFence.PrefTrans;

public class SettingsTransferService(
    ILoggingService log,
    IPrefTransLauncher launcher,
    IPermissionGrantService permissionGrantService,
    string? baseDirectory = null)
    : ISettingsTransferService
{
    private readonly string _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;

    public bool ValidatePrefTransExists(out string expectedPath)
    {
        expectedPath = Path.Combine(_baseDirectory, Constants.PrefTransExeName);
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

        return launcher.RunAndWait(prefTransPath, "store", outputFilePath,
            new LaunchCredentials(null, null, null, LaunchTokenSource.InteractiveUser), timeoutMs, pollCallback);
    }

    public SettingsTransferResult Import(string settingsFilePath, LaunchCredentials credentials, string accountSid,
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
            dbModified = permissionGrantService.EnsureExeDirectoryAccess(prefTransPath, accountSid).DatabaseModified;
            dbModified |= permissionGrantService.EnsureAccess(settingsFilePath, accountSid,
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

        if (credentials.TokenSource is LaunchTokenSource.CurrentProcess or LaunchTokenSource.InteractiveUser)
        {
            // De-elevated launch: the process runs as the interactive user who already has access to
            // settingsFilePath (granted above for InteractiveUser, or pre-existing for CurrentProcess).
            // No temp copy is needed — pass the file directly.
            var launcherResult = launcher.RunAndWait(prefTransPath, "load", settingsFilePath, credentials, timeoutMs, pollCallback);
            return launcherResult with { DatabaseModified = dbModified };
        }

        // Credentials path: copy to a shared ProgramData location and restrict access to the target
        // user so the process launched with their credentials can read the settings file.
        var sharedTempDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Constants.AppName, "temp");
        var tempSettingsPath = Path.Combine(sharedTempDir, $"rfn_import_{Guid.NewGuid():N}.json");

        try
        {
            Directory.CreateDirectory(sharedTempDir);
            File.Create(tempSettingsPath).Dispose();
            RestrictFileAccess(tempSettingsPath, accountSid);
            using (var src = File.OpenRead(settingsFilePath))
            using (var dst = new FileStream(tempSettingsPath, FileMode.Truncate, FileAccess.Write))
                src.CopyTo(dst);

            var launcherResult = launcher.RunAndWait(prefTransPath, "load", tempSettingsPath, credentials, timeoutMs, pollCallback);
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

    private void RestrictFileAccess(string filePath, string accountSid)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var security = fileInfo.GetAccessControl();

            security.SetAccessRuleProtection(true, false);

            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser != null)
                security.AddAccessRule(new FileSystemAccessRule(
                    currentUser, FileSystemRights.FullControl, AccessControlType.Allow));

            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(accountSid),
                FileSystemRights.Read | FileSystemRights.Synchronize,
                AccessControlType.Allow));

            fileInfo.SetAccessControl(security);
        }
        catch (Exception ex)
        {
            try
            {
                File.Delete(filePath);
            }
            catch
            {
            }

            throw new InvalidOperationException($"Failed to secure settings file: {ex.Message}", ex);
        }
    }
}