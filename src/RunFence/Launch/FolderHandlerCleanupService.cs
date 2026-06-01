using RunFence.Core;

namespace RunFence.Launch;

public sealed class FolderHandlerCleanupService(
    ILoggingService log,
    IHkuRootProvider hkuRootProvider,
    string? launcherPathOverride = null,
    string? shellServerPathOverride = null)
{
    public bool CleanupStaleEntries(IReadOnlyCollection<string> activeSids)
    {
        using var usersRoot = hkuRootProvider.OpenUsersRoot();
        var launcherExeName = GetLauncherExeName();
        var shellServerExeName = GetShellServerExeName();
        var activeSidSet = new HashSet<string>(activeSids, StringComparer.OrdinalIgnoreCase);
        var cleanedAny = false;

        foreach (var sidName in usersRoot.GetSubKeyNames())
        {
            if (activeSidSet.Contains(sidName))
                continue;

            cleanedAny |= CleanupStaleForSid(usersRoot, sidName, launcherExeName, shellServerExeName);
        }

        return cleanedAny;
    }

    private bool CleanupStaleForSid(
        IRegistryKey usersRoot,
        string sidName,
        string launcherExeName,
        string shellServerExeName)
    {
        var cleaned = new FolderHandlerOwnedRegistryCleaner(
                usersRoot,
                $@"{sidName}\Software\Classes",
                launcherExeName,
                shellServerExeName,
                (relativePath, ex) =>
                    log.Warn($"FolderHandlerCleanupService: failed to delete stale folder-handler key {sidName}\\Software\\Classes\\{relativePath}: {ex.Message}"))
            .UnregisterOwnedFolderHandler();

        if (cleaned)
            log.Info($"FolderHandlerCleanupService: removed stale registration for {sidName}");
        return cleaned;
    }

    private string GetLauncherExeName()
    {
        return Path.GetFileName(
            launcherPathOverride ?? Path.Combine(AppContext.BaseDirectory, PathConstants.LauncherExeName));
    }

    private string GetShellServerExeName()
    {
        return Path.GetFileName(
            shellServerPathOverride ?? Path.Combine(AppContext.BaseDirectory, PathConstants.ShellServerExeName));
    }
}
