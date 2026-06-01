using System.Text.Json;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Persistence;

/// <summary>Thrown when an import is blocked by evaluation-mode license limits.</summary>
public class EvaluationLimitException(string message) : Exception(message);

public enum AdditionalConfigImportStatus
{
    Succeeded,
    ValidationFailed,
    PersistenceFailed,
    ReloadFailed,
    RollbackFailed,
}

public sealed record AdditionalConfigImportResult(
    AdditionalConfigImportStatus Status,
    string ConfigPath,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record AdditionalConfigImportBackup(
    string ConfigPath,
    bool FileExisted,
    byte[]? FileBytes);

/// <summary>
/// Handles the business logic of importing app configs from JSON files.
/// Extracted from ConfigManagerSection to separate import concerns from UI event handling.
/// </summary>
public class ConfigImportHandler(
    IAppConfigService appConfigService,
    ISessionProvider sessionProvider,
    ILoggingService log,
    IConfigImportFileParser fileParser,
    MainConfigImportPreservationCollector preservationCollector,
    MainConfigImportEvaluationValidator evaluationValidator,
    MainConfigImportRepairService repairService,
    MainConfigImportApplyService applyService)
{
    /// <summary>
    /// Imports a JSON file as the new main config. Validates license limits, fully replaces
    /// apps/settings/accounts from the imported data with local machine-specific preservation
    /// (resolved SID names, account stubs, grants with ACEs on disk), and saves.
    /// Throws on parse errors or license violations.
    /// Returns a list of non-fatal warning messages to be shown to the user.
    /// </summary>
    public MainConfigImportResult ImportMainConfig(string path, Dictionary<string, string?>? sidResolutions)
    {
        var session = sessionProvider.GetSession();
        var database = session.Database;
        var importedDb = fileParser.ParseMainConfig(path);
        var preservation = preservationCollector.Collect(database, importedDb, sidResolutions);

        repairService.ClearImportedAppTimestamps(importedDb);
        evaluationValidator.Validate(database, importedDb);

        repairService.RepairImportedAppIdCollisions(importedDb);
        var additionalApps = repairService.GetAdditionalApps(database);
        var repairPlan = repairService.PrepareAdditionalAppIdRepairs(
            importedDb,
            additionalApps);
        repairPlan = repairPlan with
        {
            OrphanedGrantSids = repairService.GetOrphanedGrantSids(database, importedDb, additionalApps)
        };

        var warnings = ValidateMissingContainers(importedDb).ToList();
        var databaseSnapshot = database.CreateSnapshot();
        var appConfigSnapshot = appConfigService.CaptureRuntimeStateSnapshot();
        try
        {
            applyService.ApplyState(database, importedDb, repairPlan, preservation, sidResolutions);
            database.TrackingJobSids = importedDb.TrackingJobSids?.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch
        {
            database.ReplaceWithSnapshot(databaseSnapshot);
            appConfigService.RestoreRuntimeStateSnapshot(appConfigSnapshot);
            throw;
        }

        string? saveError = null;
        try
        {
            appConfigService.ReencryptAndSaveAll(
                session.CredentialStore,
                database,
                session.PinDerivedKey);
        }
        catch (Exception ex)
        {
            log.Error($"Main config import save failed for {path}", ex);
            saveError = ex.Message;
        }

        log.Info($"Main config imported from {path}");
        return new MainConfigImportResult(warnings, saveError);
    }

    private static IReadOnlyList<string> ValidateMissingContainers(AppDatabase importedDb)
    {
        var containerNames = new HashSet<string>(
            importedDb.AppContainers.Select(c => c.Name),
            StringComparer.OrdinalIgnoreCase);

        var warnings = new List<string>();
        foreach (var app in importedDb.Apps)
        {
            if (app.AppContainerName == null)
                continue;
            if (!containerNames.Contains(app.AppContainerName))
                warnings.Add($"App '{app.Name}' references container '{app.AppContainerName}' which is missing from the imported config.");
        }

        return warnings;
    }

    /// <summary>
    /// Imports a JSON file into an existing additional config file, fully replacing its
    /// apps, grants, and handler mappings.
    /// </summary>
    public AdditionalConfigImportResult ImportAdditionalConfig(string importJsonPath, string configPath)
        => ImportAdditionalConfig(importJsonPath, CaptureAdditionalConfigBackup(configPath));

    /// <summary>
    /// Captures the current additional-config file state for rollback use.
    /// </summary>
    public AdditionalConfigImportBackup CaptureAdditionalConfigBackup(string configPath)
    {
        var normalizedConfigPath = AppConfigPathHelper.NormalizePath(configPath);
        var fileExisted = File.Exists(normalizedConfigPath);
        var fileBytes = fileExisted ? File.ReadAllBytes(normalizedConfigPath) : null;
        return new AdditionalConfigImportBackup(normalizedConfigPath, fileExisted, fileBytes);
    }

    /// <summary>
    /// Restores a previously captured additional-config file state.
    /// </summary>
    public bool TryRestoreAdditionalConfigBackup(AdditionalConfigImportBackup backup)
    {
        try
        {
            RestoreAdditionalConfigBackupOrThrow(backup);
            return true;
        }
        catch (Exception ex)
        {
            log.Error($"Failed rollback for additional config import at {backup.ConfigPath}", ex);
            return false;
        }
    }

    /// <summary>
    /// Imports a JSON file into an additional config using the provided rollback backup.
    /// </summary>
    public AdditionalConfigImportResult ImportAdditionalConfig(string importJsonPath, AdditionalConfigImportBackup backup)
    {
        var session = sessionProvider.GetSession();
        var attemptedPersistImportedConfig = false;
        var normalizedConfigPath = backup.ConfigPath;

        try
        {
            var importedConfig = fileParser.ParseAdditionalConfig(importJsonPath);
            attemptedPersistImportedConfig = true;
            appConfigService.SaveImportedConfig(
                normalizedConfigPath,
                importedConfig,
                session.PinDerivedKey,
                session.CredentialStore.ArgonSalt);

            log.Info($"Additional config imported from {importJsonPath} into {normalizedConfigPath}");
            return new AdditionalConfigImportResult(
                AdditionalConfigImportStatus.Succeeded,
                normalizedConfigPath,
                [],
                []);
        }
        catch (FileNotFoundException ex)
        {
            log.Error($"Failed to validate/parse additional config import source {importJsonPath}", ex);
            var rollback = attemptedPersistImportedConfig
                ? TryRestoreAdditionalConfigBackup(backup)
                : true;
            return rollback
                ? new AdditionalConfigImportResult(
                    AdditionalConfigImportStatus.ValidationFailed,
                    normalizedConfigPath,
                    [],
                    [ex.Message])
                : new AdditionalConfigImportResult(
                    AdditionalConfigImportStatus.RollbackFailed,
                    normalizedConfigPath,
                    [],
                    [ex.Message]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Error($"Failed to persist imported additional config into {normalizedConfigPath}", ex);
            var rollback = attemptedPersistImportedConfig
                ? TryRestoreAdditionalConfigBackup(backup)
                : true;
            return rollback
                ? new AdditionalConfigImportResult(
                    AdditionalConfigImportStatus.PersistenceFailed,
                    normalizedConfigPath,
                    [],
                    [ex.Message])
                : new AdditionalConfigImportResult(
                    AdditionalConfigImportStatus.RollbackFailed,
                    normalizedConfigPath,
                    [],
                    [ex.Message]);
        }
        catch (JsonException ex)
        {
            log.Error($"Failed to validate/parse additional config import source {importJsonPath}", ex);
            var rollback = attemptedPersistImportedConfig
                ? TryRestoreAdditionalConfigBackup(backup)
                : true;
            return rollback
                ? new AdditionalConfigImportResult(
                    AdditionalConfigImportStatus.ValidationFailed,
                    normalizedConfigPath,
                    [],
                    [ex.Message])
                : new AdditionalConfigImportResult(
                    AdditionalConfigImportStatus.RollbackFailed,
                    normalizedConfigPath,
                    [],
                    [ex.Message]);
        }
        catch (InvalidAppIdException ex)
        {
            log.Error($"Failed to validate/parse additional config import source {importJsonPath}", ex);
            var rollback = attemptedPersistImportedConfig
                ? TryRestoreAdditionalConfigBackup(backup)
                : true;
            return rollback
                ? new AdditionalConfigImportResult(
                    AdditionalConfigImportStatus.ValidationFailed,
                    normalizedConfigPath,
                    [],
                    [ex.Message])
                : new AdditionalConfigImportResult(
                    AdditionalConfigImportStatus.RollbackFailed,
                    normalizedConfigPath,
                    [],
                    [ex.Message]);
        }
        catch (Exception ex)
        {
            log.Error($"Additional config import failed for target {normalizedConfigPath}", ex);
            var rollback = attemptedPersistImportedConfig
                ? TryRestoreAdditionalConfigBackup(backup)
                : true;
            var status = attemptedPersistImportedConfig
                ? AdditionalConfigImportStatus.PersistenceFailed
                : AdditionalConfigImportStatus.ValidationFailed;
            return rollback
                ? new AdditionalConfigImportResult(
                    status,
                    normalizedConfigPath,
                    [],
                    [ex.Message])
                : new AdditionalConfigImportResult(
                    AdditionalConfigImportStatus.RollbackFailed,
                    normalizedConfigPath,
                    [],
                    [ex.Message]);
        }
    }

    private static void RestoreAdditionalConfigBackupOrThrow(AdditionalConfigImportBackup backup)
    {
        if (backup.FileExisted && backup.FileBytes != null)
            File.WriteAllBytes(backup.ConfigPath, backup.FileBytes);
        else if (!backup.FileExisted && File.Exists(backup.ConfigPath))
            File.Delete(backup.ConfigPath);
    }
}
