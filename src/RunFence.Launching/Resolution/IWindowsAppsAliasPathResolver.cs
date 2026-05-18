namespace RunFence.Launching.Resolution;

public interface IWindowsAppsAliasPathResolver
{
    string? TryResolveForUserSid(string nameOrPath, string targetUserSid);

    bool IsWindowsAppsAliasPath(string path);
}
