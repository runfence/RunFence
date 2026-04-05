namespace RunFence.Launch;

public interface IFolderHandlerService
{
    void Register(string accountSid);
    void Unregister(string accountSid);
    void UnregisterAll();
    bool IsRegistered(string accountSid);
    void CleanupStaleRegistrations();
}