namespace RunFence.Core.Models;

public class OrphanedSid
{
    public const int MaxSamplePaths = 20;

    public string Sid { get; init; } = string.Empty;
    public int AceCount { get; set; }
    public int OwnerCount { get; set; }
    public string? GuessedName { get; set; }
    public List<string> SamplePaths { get; } = new();
}