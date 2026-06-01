namespace RunFence.Core.Models;

public class OrphanedSid
{
    public const int MaxSamplePaths = 20;

    public string Sid { get; init; } = string.Empty;
    public OrphanedSidClassification Classification { get; set; } = OrphanedSidClassification.ConfirmedOrphaned;
    public int AceCount { get; set; }
    public int OwnerCount { get; set; }
    public List<string> AceSamplePaths { get; } = new();
    public List<string> OwnerSamplePaths { get; } = new();
}
