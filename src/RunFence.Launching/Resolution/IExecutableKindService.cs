namespace RunFence.Launching.Resolution;

public interface IExecutableKindService
{
    bool IsKnownBrowserExe(string path);

    bool IsUwpExeFile(string path);

    bool SuggestsBasicPrivilegeLevel(string path);
}
