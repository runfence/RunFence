using Microsoft.Win32;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Launch;

public sealed class FolderHandlerRegistrationWriter(
    IHkuRootProvider hkuRootProvider,
    Func<FolderHandlerRegistrationChangeTracker> changeTrackerFactory,
    FolderHandlerRunOnceMaintenance runOnceMaintenance,
    string? launcherPathOverride = null,
    string? shellServerPathOverride = null)
{
    private const string DirectoryShellPath = @"Directory\shell";
    private const string DirectoryOpenCommandPath = @"Directory\shell\open\command";
    private const string DirectoryExploreCommandPath = @"Directory\shell\explore\command";
    private const string FolderOpenCommandPath = @"Folder\shell\open\command";

    public void Unregister(string accountSid)
    {
        using var usersRoot = hkuRootProvider.OpenUsersRoot();
        CreateOwnedRegistryCleaner(usersRoot, accountSid).UnregisterOwnedFolderHandler();
        runOnceMaintenance.Remove(usersRoot, accountSid);
    }

    public bool HasOwnedRegistration(string accountSid)
    {
        using var usersRoot = hkuRootProvider.OpenUsersRoot();
        var cleaner = CreateOwnedRegistryCleaner(usersRoot, accountSid);
        return cleaner.HasOwnedCommandValue(FolderHandlerOwnedRegistryCleaner.DirectoryOpenCommandPath)
               && cleaner.HasOwnedCommandValue(FolderHandlerOwnedRegistryCleaner.DirectoryExploreCommandPath)
               && cleaner.HasOwnedCommandValue(FolderHandlerOwnedRegistryCleaner.FolderOpenCommandPath);
    }

    public IReadOnlyList<string> GetOwnedRegistrationSids()
    {
        using var usersRoot = hkuRootProvider.OpenUsersRoot();
        var ownedSids = new List<string>();
        foreach (var sidName in usersRoot.GetSubKeyNames())
        {
            var cleaner = CreateOwnedRegistryCleaner(usersRoot, sidName);
            if (cleaner.HasOwnedCommandValue(FolderHandlerOwnedRegistryCleaner.DirectoryOpenCommandPath)
                && cleaner.HasOwnedCommandValue(FolderHandlerOwnedRegistryCleaner.DirectoryExploreCommandPath)
                && cleaner.HasOwnedCommandValue(FolderHandlerOwnedRegistryCleaner.FolderOpenCommandPath))
            {
                ownedSids.Add(sidName);
            }
        }

        return ownedSids;
    }

    public FolderHandlerRegistrationMaintenanceResult EnsureOwnedRegistration(
        string accountSid,
        string launcherPath,
        string runOnceCommandLine)
    {
        using var usersRoot = hkuRootProvider.OpenUsersRoot();
        var cleaner = CreateOwnedRegistryCleaner(usersRoot, accountSid);
        var hadOwnedRegistrationBeforeCall =
            cleaner.HasOwnedCommandValue(FolderHandlerOwnedRegistryCleaner.DirectoryOpenCommandPath)
            && cleaner.HasOwnedCommandValue(FolderHandlerOwnedRegistryCleaner.DirectoryExploreCommandPath)
            && cleaner.HasOwnedCommandValue(FolderHandlerOwnedRegistryCleaner.FolderOpenCommandPath);
        var tracker = changeTrackerFactory().Initialize(usersRoot, accountSid);
        var commandValue = $"\"{launcherPath}\" --open-folder \"%V\"";

        try
        {
            EnsureCommandValue(tracker, DirectoryOpenCommandPath, commandValue);
            EnsureCommandValue(tracker, DirectoryExploreCommandPath, commandValue);
            EnsureDirectoryShellState(tracker);
            EnsureFolderCommandState(tracker, commandValue);
            runOnceMaintenance.EnsureState(tracker, runOnceCommandLine);

            return tracker.BuildResult(hadOwnedRegistrationBeforeCall);
        }
        catch (Exception ex)
        {
            throw new FolderHandlerRegistrationMaintenanceException(
                $"Failed to maintain owned folder-handler registration for {accountSid}.",
                ex,
                tracker.BuildResult(hadOwnedRegistrationBeforeCall));
        }
    }

    private static void EnsureCommandValue(FolderHandlerRegistrationChangeTracker tracker, string subKeyPath, string commandValue)
    {
        tracker.SetValue(subKeyPath, null, commandValue, RegistryValueKind.String, isRunOnceValue: false);
    }

    private static void EnsureDirectoryShellState(FolderHandlerRegistrationChangeTracker tracker)
    {
        tracker.EnsureKey(DirectoryShellPath);
        using var shellKey = tracker.OpenWritableClassesKey(DirectoryShellPath);
        if (!ValueExists(shellKey, PathConstants.RunFenceFallbackValueName))
        {
            tracker.SetValue(
                DirectoryShellPath,
                PathConstants.RunFenceFallbackValueName,
                shellKey.GetValue(null) as string ?? string.Empty,
                RegistryValueKind.String,
                isRunOnceValue: false);
        }

        tracker.SetValue(DirectoryShellPath, null, "open", RegistryValueKind.String, isRunOnceValue: false);
    }

    private static void EnsureFolderCommandState(FolderHandlerRegistrationChangeTracker tracker, string commandValue)
    {
        tracker.SetValue(FolderOpenCommandPath, null, commandValue, RegistryValueKind.String, isRunOnceValue: false);
        tracker.SetValue(FolderOpenCommandPath, "DelegateExecute", string.Empty, RegistryValueKind.String, isRunOnceValue: false);
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

    private FolderHandlerOwnedRegistryCleaner CreateOwnedRegistryCleaner(
        IRegistryKey usersRoot,
        string sidName,
        Action<string, Exception>? onError = null)
    {
        return new FolderHandlerOwnedRegistryCleaner(
            usersRoot,
            $@"{sidName}\Software\Classes",
            GetLauncherExeName(),
            GetShellServerExeName(),
            onError);
    }

    private static bool ValueExists(IRegistryKey key, string? valueName)
        => Array.Exists(
            key.GetValueNames(),
            existingName => string.Equals(
                existingName,
                FolderHandlerRegistryPathMapper.NormalizeValueName(valueName),
                StringComparison.OrdinalIgnoreCase));
}
