namespace RunFence.Launch;

public interface IFolderHandlerService
{
    FolderHandlerRegistrationResult Register(string accountSid);
    void Unregister(string accountSid);
    void UnregisterAll();
    bool IsRegistered(string accountSid);
    IReadOnlyList<string> CaptureCleanupSidSnapshot();
    void CleanupStaleRegistrations();
    void CleanupStaleRegistrations(IReadOnlyCollection<string> rawSessionSids);
}
