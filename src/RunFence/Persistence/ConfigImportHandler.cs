using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Persistence;

/// <summary>Thrown when an import is blocked by evaluation-mode license limits.</summary>
public class EvaluationLimitException(string message) : Exception(message);

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
    MainConfigImportApplyService applyService,
    MainConfigImportSaveHelper saveHelper)
{
    /// <summary>
    /// Imports a JSON file as the new main config. Validates license limits, fully replaces
    /// apps/settings/accounts from the imported data with local machine-specific preservation
    /// (resolved SID names, account stubs, grants with ACEs on disk), and saves.
    /// Throws on parse errors or license violations.
    /// Returns a list of non-fatal warning messages to be shown to the user.
    /// </summary>
    public IReadOnlyList<string> ImportMainConfig(string path, Dictionary<string, string?>? sidResolutions)
    {
        var session = sessionProvider.GetSession();
        var database = session.Database;
        var importedDb = fileParser.ParseMainConfig(path);
        var preservation = preservationCollector.Collect(database, importedDb, sidResolutions);

        repairService.ClearImportedAppTimestamps(importedDb);
        evaluationValidator.Validate(database, importedDb);

        var additionalApps = repairService.GetAdditionalApps(database);
        repairService.RemoveOrphanedGrantAces(database, importedDb, additionalApps);
        repairService.RepairImportedAppIdCollisions(importedDb);
        repairService.RepairAdditionalAppIdCollisions(importedDb, additionalApps);

        var warnings = ValidateMissingContainers(importedDb);

        applyService.Apply(database, importedDb, additionalApps, preservation, sidResolutions);
        saveHelper.Save(session, database);

        log.Info($"Main config imported from {path}");
        return warnings;
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
    public void ImportAdditionalConfig(string importJsonPath, string configPath)
    {
        var session = sessionProvider.GetSession();
        var importedConfig = fileParser.ParseAdditionalConfig(importJsonPath);

        using var scope = session.PinDerivedKey.Unprotect();
        appConfigService.SaveImportedConfig(
            configPath,
            importedConfig,
            scope.Data,
            session.CredentialStore.ArgonSalt);

        log.Info($"Additional config imported from {importJsonPath} into {configPath}");
    }
}
