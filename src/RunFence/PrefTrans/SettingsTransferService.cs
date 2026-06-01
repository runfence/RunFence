using System.Security.AccessControl;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Acl.Permissions;

namespace RunFence.PrefTrans;

public class SettingsTransferService(
    ILoggingService log,
    IPrefTransLauncher launcher,
    Func<ISettingsTransferAccessGrantService> accessGrantServiceFactory,
    ISettingsTransferStagingService stagingService,
    IInteractiveUserResolver interactiveUserResolver,
    IProgramDataPathPolicyService programDataPathPolicyService,
    IProgramDataManagedObjectRepairService programDataManagedObjectRepairService,
    string baseDirectory)
    : ISettingsTransferService
{
    public string BaseDirectory { get; } = baseDirectory;

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

        bool requiresStaging = programDataPathPolicyService.IsUnderRoot(outputFilePath);
        var accessGrantService = accessGrantServiceFactory();
        bool databaseModified = false;
        if (!requiresStaging &&
            string.Equals(interactiveSid, SidResolutionHelper.GetCurrentUserSid(), StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var exeDir = Path.GetDirectoryName(prefTransPath)!;
                databaseModified |= EnsureDurableTransferToolAccess(accessGrantService, interactiveSid, exeDir, FileSystemRights.ReadAndExecute);
                databaseModified |= EnsureTransferAccess(accessGrantService, interactiveSid, outputFilePath, FileSystemRights.Write | FileSystemRights.Synchronize, isDirectory: false);
                var launcherResult = launcher.RunAndWait(prefTransPath, "store", outputFilePath, interactiveSid, timeoutMs, pollCallback);
                return launcherResult with { DatabaseModified = launcherResult.DatabaseModified || databaseModified };
            }
            catch (OperationCanceledException)
            {
                return new SettingsTransferResult(false, "Permission grant was declined.");
            }
            catch (Exception ex)
            {
                log.Error("Settings export failed", ex);
                return new SettingsTransferResult(false, $"Export failed: {ex.Message}");
            }
            finally
            {
                accessGrantService.CleanupTemporaryGrant();
            }
        }

        var tempDirPath = stagingService.CreateSharedTempDirectoryPath();
        var tempOutputPath = Path.Combine(tempDirPath, "settings.json");

        try
        {
            stagingService.CreateRestrictedExportDirectory(tempDirPath, interactiveSid);
            databaseModified |= EnsureDurableTransferToolAccess(accessGrantService, interactiveSid, Path.GetDirectoryName(prefTransPath)!, FileSystemRights.ReadAndExecute);

            var launcherResult = launcher.RunAndWait(prefTransPath, "store", tempOutputPath, interactiveSid, timeoutMs, pollCallback);
            if (!launcherResult.Success)
                return launcherResult with { DatabaseModified = launcherResult.DatabaseModified || databaseModified };

            programDataManagedObjectRepairService.EnsureManagedFileOwner(tempOutputPath);
            stagingService.CopyExportFileToDestination(tempOutputPath, outputFilePath);
            return launcherResult with { DatabaseModified = launcherResult.DatabaseModified || databaseModified };
        }
        catch (OperationCanceledException)
        {
            return new SettingsTransferResult(false, "Permission grant was declined.");
        }
        catch (Exception ex)
        {
            log.Error("Settings export failed", ex);
            return new SettingsTransferResult(false, $"Export failed: {ex.Message}");
        }
        finally
        {
            accessGrantService.CleanupTemporaryGrant();
            stagingService.TryDeleteTempDirectory(tempDirPath);
        }
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

        var accessGrantService = accessGrantServiceFactory();
        bool databaseModified = false;
        var interactiveUserSid = interactiveUserResolver.GetInteractiveUserSid();
        if (string.Equals(accountSid, SidResolutionHelper.GetCurrentUserSid(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(accountSid, interactiveUserSid, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var exeDir = Path.GetDirectoryName(prefTransPath)!;
                databaseModified |= EnsureDurableTransferToolAccess(accessGrantService, accountSid, exeDir, FileSystemRights.ReadAndExecute);
                databaseModified |= EnsureTransferAccess(accessGrantService, accountSid, settingsFilePath, FileSystemRights.Read | FileSystemRights.Synchronize, isDirectory: false);
                var launcherResult = launcher.RunAndWait(prefTransPath, "load", settingsFilePath, accountSid, timeoutMs, pollCallback);
                return launcherResult with { DatabaseModified = launcherResult.DatabaseModified || databaseModified };
            }
            catch (OperationCanceledException)
            {
                return new SettingsTransferResult(false, "Permission grant was declined.");
            }
            catch (Exception ex)
            {
                log.Error("Settings import failed", ex);
                return new SettingsTransferResult(false, $"Import failed: {ex.Message}");
            }
            finally
            {
                accessGrantService.CleanupTemporaryGrant();
            }
        }

        if (interactiveUserSid == null)
        {
            var noSessionMsg = "Cannot import desktop settings through temporary staging: no interactive user session is active.";
            log.Error(noSessionMsg);
            return new SettingsTransferResult(false, noSessionMsg);
        }

        var tempSettingsPath = stagingService.CreateSharedTempFilePath("json");
        var tempDirectory = Path.GetDirectoryName(tempSettingsPath);
        if (tempDirectory == null)
        {
            log.Error("Failed to create temporary import path.");
            return new SettingsTransferResult(false, "Failed to create temporary import path.");
        }

        try
        {
            var exeDir = Path.GetDirectoryName(prefTransPath)!;
            databaseModified |= EnsureDurableTransferToolAccess(accessGrantService, accountSid, exeDir, FileSystemRights.ReadAndExecute);
            stagingService.CopyImportFileToRestrictedTemp(settingsFilePath, tempSettingsPath, interactiveUserSid);

            var cleanupResult = accessGrantService.TryEnsureAccessForCleanup(
                accountSid,
                tempSettingsPath,
                FileSystemRights.Read | FileSystemRights.Synchronize,
                isDirectory: false);
            if (!cleanupResult.Succeeded)
                throw new InvalidOperationException(cleanupResult.WarningMessage ?? $"Failed to ensure cleanup access to '{tempSettingsPath}'.");

            if (!string.IsNullOrWhiteSpace(cleanupResult.WarningMessage))
                log.Warn(cleanupResult.WarningMessage);

            var launcherResult = launcher.RunAndWait(prefTransPath, "load", tempSettingsPath, accountSid, timeoutMs, pollCallback);
            return launcherResult with { DatabaseModified = launcherResult.DatabaseModified || databaseModified };
        }
        catch (OperationCanceledException)
        {
            return new SettingsTransferResult(false, "Permission grant was declined.");
        }
        catch (Exception ex)
        {
            log.Error("Settings import failed", ex);
            return new SettingsTransferResult(false, $"Import failed: {ex.Message}");
        }
        finally
        {
            accessGrantService.CleanupTemporaryGrant();
            stagingService.TryDeleteTempFile(tempSettingsPath);
            stagingService.TryDeleteTempDirectory(tempDirectory);
        }
    }

    private bool EnsureTransferAccess(
        ISettingsTransferAccessGrantService accessGrantService,
        string sid,
        string path,
        FileSystemRights rights,
        bool isDirectory)
    {
        var result = accessGrantService.TryEnsureAccess(sid, path, rights, isDirectory);
        if (!result.Succeeded)
            throw new InvalidOperationException(result.WarningMessage ?? $"Failed to ensure access to '{path}'.");

        if (!string.IsNullOrWhiteSpace(result.WarningMessage))
            log.Warn(result.WarningMessage);
        return result.GrantCreated;
    }

    private bool EnsureDurableTransferToolAccess(
        ISettingsTransferAccessGrantService accessGrantService,
        string sid,
        string path,
        FileSystemRights rights)
    {
        var result = accessGrantService.TryEnsureDurableAccess(sid, path, rights);
        if (!result.Succeeded)
            throw new InvalidOperationException(result.WarningMessage ?? $"Failed to ensure durable access to '{path}'.");

        if (!string.IsNullOrWhiteSpace(result.WarningMessage))
            log.Warn(result.WarningMessage);
        return result.GrantCreated;
    }
}
