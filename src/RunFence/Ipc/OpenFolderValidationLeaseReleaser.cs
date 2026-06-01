using RunFence.Infrastructure;

namespace RunFence.Ipc;

public class OpenFolderValidationLeaseReleaser(IAsyncDelay asyncDelay) : IOpenFolderValidationLeaseReleaser
{
    private static readonly TimeSpan SuccessfulOpenLeaseDuration = TimeSpan.FromSeconds(5);

    public Task ReleaseAfterSuccessfulOpen(DirectoryValidationHandle validation)
        => ReleaseValidationLeaseAsync(validation);

    private async Task ReleaseValidationLeaseAsync(DirectoryValidationHandle validation)
    {
        try
        {
            await asyncDelay.Delay(SuccessfulOpenLeaseDuration).ConfigureAwait(false);
        }
        finally
        {
            validation.Dispose();
        }
    }
}
