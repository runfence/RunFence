namespace RunFence.PrefTrans;

public interface IPrefTransLauncher
{
    SettingsTransferResult RunAndWait(string prefTransPath, string command, string filePath,
        string accountSid, int timeoutMs, Action? pollCallback);
}
