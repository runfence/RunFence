namespace RunFence.Ipc;

public interface IOpenFolderValidationLeaseReleaser
{
    Task ReleaseAfterSuccessfulOpen(DirectoryValidationHandle validation);
}
