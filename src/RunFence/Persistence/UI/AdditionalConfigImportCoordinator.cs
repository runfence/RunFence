using RunFence.Core;

namespace RunFence.Persistence.UI;

/// <summary>
/// Coordinates additional-config import for loaded configs by unloading/reloading around
/// persistence-only import logic in <see cref="ConfigImportHandler"/>.
/// </summary>
public class AdditionalConfigImportCoordinator(
    IAppConfigService appConfigService,
    IAdditionalConfigLoadService configLoadService,
    ConfigImportHandler importHandler,
    ILoggingService log)
{
    public AdditionalConfigImportResult ImportAdditionalConfig(string importJsonPath, string configPath)
    {
        var normalizedConfigPath = Path.GetFullPath(configPath);
        var loadedPaths = appConfigService.GetLoadedConfigPaths() ?? [];
        var wasLoaded = loadedPaths.Any(path =>
            string.Equals(path, normalizedConfigPath, StringComparison.OrdinalIgnoreCase));
        var backup = importHandler.CaptureAdditionalConfigBackup(normalizedConfigPath);

        if (wasLoaded && !configLoadService.UnloadApps(normalizedConfigPath))
        {
            return new AdditionalConfigImportResult(
                AdditionalConfigImportStatus.ValidationFailed,
                normalizedConfigPath,
                [],
                ["Failed to unload the target config before import."]);
        }

        var importResult = importHandler.ImportAdditionalConfig(importJsonPath, backup);
        if (importResult.Status != AdditionalConfigImportStatus.Succeeded)
            return HandleImportFailure(importResult, wasLoaded, normalizedConfigPath);

        if (!wasLoaded)
            return importResult;

        var reloadImported = configLoadService.LoadApps(normalizedConfigPath);
        if (reloadImported.Succeeded)
            return importResult;

        log.Warn($"Failed to reload imported config at {normalizedConfigPath}");
        var reloadError = $"Failed to reload imported config: {reloadImported.ErrorMessage ?? "Unknown error"}";
        if (!importHandler.TryRestoreAdditionalConfigBackup(backup))
        {
            return new AdditionalConfigImportResult(
                AdditionalConfigImportStatus.RollbackFailed,
                normalizedConfigPath,
                [],
                [reloadError]);
        }

        var reloadPrevious = configLoadService.LoadApps(normalizedConfigPath);
        if (!reloadPrevious.Succeeded)
        {
            return new AdditionalConfigImportResult(
                AdditionalConfigImportStatus.RollbackFailed,
                normalizedConfigPath,
                [],
                [reloadError]);
        }

        return new AdditionalConfigImportResult(
            AdditionalConfigImportStatus.ReloadFailed,
            normalizedConfigPath,
            [],
            [reloadError]);
    }

    private AdditionalConfigImportResult HandleImportFailure(
        AdditionalConfigImportResult importResult,
        bool wasLoaded,
        string normalizedConfigPath)
    {
        if (!wasLoaded)
            return importResult;

        var reloadPrevious = configLoadService.LoadApps(normalizedConfigPath);
        if (reloadPrevious.Succeeded)
            return importResult;

        var previousReloadError = reloadPrevious.ErrorMessage ?? "Unknown error";
        log.Warn(
            $"Additional config import rollback failed to reload previously loaded config at {normalizedConfigPath}: {previousReloadError}");
        var errors = importResult.Errors.Count > 0
            ? importResult.Errors.Concat([$"Failed to reload previous config: {previousReloadError}"]).ToList()
            : [$"Failed to reload previous config: {previousReloadError}"];
        return new AdditionalConfigImportResult(
            AdditionalConfigImportStatus.RollbackFailed,
            normalizedConfigPath,
            [],
            errors);
    }
}
