namespace RunFence.Account.OrphanedProfiles;

public interface IProfileSizeCalculator
{
    long CalculateSizeBytes(string profilePath, IProgress<long>? progress, CancellationToken cancellationToken);
}
