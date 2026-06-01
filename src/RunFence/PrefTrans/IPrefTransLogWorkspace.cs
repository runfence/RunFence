namespace RunFence.PrefTrans;

public interface IPrefTransLogWorkspace
{
    PrefTransLogWorkspaceResult CreateLogFile(string accountSid);
    string ReadLogFile(string path);
    void TryDeleteLogFile(string path);
}
