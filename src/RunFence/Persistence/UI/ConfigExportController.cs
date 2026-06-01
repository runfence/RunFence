using System.Text;
using System.Text.Json;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Persistence.UI;

public class ConfigExportController(
    IAppConfigService appConfigService,
    IAppFilter appFilter,
    ISessionProvider sessionProvider,
    IFileContentService fileContentService,
    ILoggingService log)
{
    public ConfigExportResult Export(string? configPath, string exportPath)
    {
        try
        {
            var database = sessionProvider.GetSession().Database;
            var exportConfig = appConfigService.GetConfigForExport(configPath, database);
            string json;
            if (configPath == null)
            {
                var mainDb = appFilter.FilterForMainConfig(database);
                mainDb.Apps = exportConfig.Apps;
                mainDb.Settings.HandlerMappings = exportConfig.HandlerMappings != null
                    ? new Dictionary<string, HandlerMappingEntry>(exportConfig.HandlerMappings, StringComparer.OrdinalIgnoreCase)
                    : null;
                json = JsonSerializer.Serialize(mainDb, JsonDefaults.Options);
            }
            else
            {
                json = JsonSerializer.Serialize(exportConfig, JsonDefaults.Options);
            }

            fileContentService.WriteAllText(exportPath, json, Encoding.UTF8);
            return new ConfigExportResult(true, Path.GetFileName(exportPath), null);
        }
        catch (Exception ex)
        {
            log.Error("Config export failed", ex);
            return new ConfigExportResult(false, null, ex.Message);
        }
    }
}

public sealed record ConfigExportResult(
    bool Succeeded,
    string? ExportedFileName,
    string? ErrorMessage);
