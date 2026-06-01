namespace RunFence.Acl;

public sealed class GrantIntentStoreIdentity
{
    public GrantIntentStoreIdentity(string? configPath)
    {
        ConfigPath = configPath == null ? null : Path.GetFullPath(configPath);
    }

    public string? ConfigPath { get; }

    public bool IsMainStore => ConfigPath == null;
}
