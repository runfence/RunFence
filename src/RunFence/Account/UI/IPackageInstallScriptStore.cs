namespace RunFence.Account.UI;

public interface IPackageInstallScriptStore
{
    string CreateScript(string command, string userSid);
    void Delete(string path);
    void CleanupStaleScripts();
}
