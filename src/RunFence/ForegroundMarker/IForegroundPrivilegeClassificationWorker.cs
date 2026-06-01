namespace RunFence.ForegroundMarker;

public interface IForegroundPrivilegeClassificationWorker
{
    Task<ForegroundPrivilegeClassificationResult> ClassifyAsync(
        ForegroundPrivilegeClassificationRequest request,
        CancellationToken cancellationToken);
}
