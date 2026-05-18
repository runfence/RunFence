using RunFence.Core.Models;

namespace RunFence.Persistence;

public sealed class GrantIntentRestoreLocation
{
    public GrantIntentRestoreLocation(string? configPath, GrantedPathEntry entry)
    {
        ConfigPath = configPath == null ? null : Path.GetFullPath(configPath);
        Entry = entry.Clone();
    }

    public string? ConfigPath { get; }

    public GrantedPathEntry Entry { get; }
}
