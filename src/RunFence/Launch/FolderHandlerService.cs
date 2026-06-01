namespace RunFence.Launch;

public class FolderHandlerService(
    FolderHandlerRegistrationWorkflow registrationWorkflow,
    FolderHandlerCleanupWorkflow cleanupWorkflow)
    : IFolderHandlerService
{
    public bool IsRegistered(string accountSid)
        => registrationWorkflow.IsRegistered(accountSid);

    public FolderHandlerRegistrationResult Register(string accountSid)
        => registrationWorkflow.Register(accountSid);

    public void Unregister(string accountSid)
        => registrationWorkflow.Unregister(accountSid);

    public void UnregisterAll()
        => cleanupWorkflow.UnregisterAll();

    public IReadOnlyList<string> CaptureCleanupSidSnapshot()
        => cleanupWorkflow.CaptureCleanupSidSnapshot();

    public void CleanupStaleRegistrations()
        => cleanupWorkflow.CleanupStaleRegistrations();

    public void CleanupStaleRegistrations(IReadOnlyCollection<string> rawSessionSids)
        => cleanupWorkflow.CleanupStaleRegistrations(rawSessionSids);
}
