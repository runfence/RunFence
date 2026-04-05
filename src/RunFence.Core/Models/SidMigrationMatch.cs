namespace RunFence.Core.Models;

[Flags]
public enum SidMigrationMatchType
{
    None = 0,
    Ace = 1,
    Owner = 2
}

public class SidMigrationMatch
{
    public string Path { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public SidMigrationMatchType MatchType { get; init; }
    public IReadOnlyDictionary<string, int> AceCountByOldSid { get; init; } = new Dictionary<string, int>();
    public string? OwnerOldSid { get; init; }
}