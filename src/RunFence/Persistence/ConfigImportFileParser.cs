using System.Text.Json;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Persistence;

public class ConfigImportFileParser : IConfigImportFileParser
{
    public AppDatabase ParseMainConfig(string path)
    {
        if (new FileInfo(path).Length > 50 * 1024 * 1024)
            throw new InvalidOperationException("Config file too large.");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppDatabase>(json, JsonDefaults.Options)
               ?? throw new InvalidOperationException("Failed to parse config file.");
    }

    public AppConfig ParseAdditionalConfig(string path)
    {
        if (new FileInfo(path).Length > 50 * 1024 * 1024)
            throw new InvalidOperationException("Config file too large.");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppConfig>(json, JsonDefaults.Options)
               ?? throw new InvalidOperationException("Failed to parse config file.");
    }
}
