using RunFence.Launch;

namespace RunFence.PrefTrans;

public interface IPrefTransLauncher
{
    SettingsTransferResult RunAndWait(string prefTransPath, string command, string filePath,
        LaunchCredentials credentials, int timeoutMs, Action? pollCallback);
}