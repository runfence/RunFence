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
        var normalizedConfigPath = AppConfigPathHelper.NormalizePath(configPath);
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
                [
                    reloadError,
                    $"Failed to reload/parse previous config: {reloadPrevious.ErrorMessage ?? "Unknown error"}"
                ]);
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
        return new AdditionalConfigImportResult(
            AdditionalConfigImportStatus.RollbackFailed,
            normalizedConfigPath,
            [],
            [.. importResult.Errors, $"Failed to reload/parse previous config: {previousReloadError}"]);
    }
}
