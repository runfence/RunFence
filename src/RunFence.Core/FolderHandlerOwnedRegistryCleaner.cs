namespace RunFence.Core;

public sealed class FolderHandlerOwnedRegistryCleaner(
    IRegistryKey root,
    string classesRootPath,
    string launcherExeName,
    string shellServerExeName,
    Action<string, Exception>? onError = null)
{
    public const string DirectoryOpenCommandPath = @"Directory\shell\open\command";
    public const string DirectoryExploreCommandPath = @"Directory\shell\explore\command";
    public const string FolderOpenCommandPath = @"Folder\shell\open\command";
    public const string DirectoryShellPath = @"Directory\shell";
    public const string ShellWindowsClsidRelativePath =
        @"CLSID\{9BA05972-F6A8-11CF-A442-00A0C90A8F39}";

    public bool UnregisterOwnedFolderHandler()
    {
        var restoreDirectoryShellFallback = ShouldRestoreDirectoryShellFallback();
        var changed = false;

        changed |= TryDeleteOwnedCommandKey(DirectoryOpenCommandPath);
        changed |= TryDeleteOwnedCommandKey(DirectoryExploreCommandPath);
        changed |= TryDeleteOwnedCommandKey(FolderOpenCommandPath);
        changed |= TryDeleteOwnedCommandKey(ShellWindowsClsidRelativePath + @"\shell\open\command");
        changed |= TryDeleteOwnedClsidOverride();

        if (restoreDirectoryShellFallback)
            changed |= TryRestoreDirectoryShellFallback();
        else
            changed |= TryDeleteDirectoryShellFallback();

        return changed;
    }

    public bool HasOwnedCommandValue(string relativeSubKeyPath)
    {
        try
        {
        using var key = root.OpenSubKey(GetClassesRegistryPath(relativeSubKeyPath));
            return key?.GetValue(null) is string value &&
                   value.Contains(launcherExeName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public bool TryDeleteOwnedCommandKey(string relativeSubKeyPath)
    {
        try
        {
            var fullSubKeyPath = GetClassesRegistryPath(relativeSubKeyPath);
            using var key = root.OpenSubKey(fullSubKeyPath);
            if (key?.GetValue(null) is not string value)
                return false;
            if (!value.Contains(launcherExeName, StringComparison.OrdinalIgnoreCase))
                return false;

            root.DeleteSubKeyTree(fullSubKeyPath, throwOnMissingSubKey: false);
            DeleteEmptyParentChain(relativeSubKeyPath);
            return true;
        }
        catch (Exception ex)
        {
            onError?.Invoke(relativeSubKeyPath, ex);
            return false;
        }
    }

    public bool TryDeleteOwnedClsidOverride()
    {
        try
        {
            var fullLocalServerPath = GetClassesRegistryPath(ShellWindowsClsidRelativePath + @"\LocalServer32");
            using var key = root.OpenSubKey(fullLocalServerPath);
            if (key?.GetValue(null) is not string value)
                return false;

            if (!value.Contains(shellServerExeName, StringComparison.OrdinalIgnoreCase) &&
                !value.Contains(launcherExeName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            root.DeleteSubKeyTree(fullLocalServerPath, throwOnMissingSubKey: false);
            DeleteEmptySubKey(ShellWindowsClsidRelativePath);
            return true;
        }
        catch (Exception ex)
        {
            onError?.Invoke(ShellWindowsClsidRelativePath + @"\LocalServer32", ex);
            return false;
        }
    }

    private bool ShouldRestoreDirectoryShellFallback()
    {
        try
        {
            using var shellKey = root.OpenSubKey(GetClassesRegistryPath(DirectoryShellPath));
            if (shellKey?.GetValue(null) is not string verb || string.IsNullOrWhiteSpace(verb))
                return false;

            if (HasOwnedCommandValue($@"{DirectoryShellPath}\{verb}\command"))
                return true;

            if (shellKey.GetValue(PathConstants.RunFenceFallbackValueName) is not string)
                return false;

            if (!verb.Equals("open", StringComparison.OrdinalIgnoreCase) &&
                !verb.Equals("explore", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            using var commandKey = root.OpenSubKey(GetClassesRegistryPath($@"{DirectoryShellPath}\{verb}\command"));
            return commandKey?.GetValue(null) is not string commandValue ||
                   string.IsNullOrWhiteSpace(commandValue);
        }
        catch
        {
            return false;
        }
    }

    private bool TryRestoreDirectoryShellFallback()
    {
        try
        {
            var shellKeyPath = GetClassesRegistryPath(DirectoryShellPath);
            using var shellKey = root.OpenSubKey(shellKeyPath, writable: true);
            if (shellKey?.GetValue(PathConstants.RunFenceFallbackValueName) is not string fallback)
            {
                if (shellKey?.GetValue(null) == null)
                    return false;

                shellKey.DeleteValue(string.Empty, throwOnMissingValue: false);
                DeleteEmptySubKey(DirectoryShellPath);
                return true;
            }

            if (fallback.Length == 0)
                shellKey.DeleteValue(string.Empty, throwOnMissingValue: false);
            else
                shellKey.SetValue(null, fallback);

            shellKey.DeleteValue(PathConstants.RunFenceFallbackValueName, throwOnMissingValue: false);
            DeleteEmptySubKey(DirectoryShellPath);
            return true;
        }
        catch (Exception ex)
        {
            onError?.Invoke(DirectoryShellPath, ex);
            return false;
        }
    }

    private bool TryDeleteDirectoryShellFallback()
    {
        try
        {
            var shellKeyPath = GetClassesRegistryPath(DirectoryShellPath);
            using var shellKey = root.OpenSubKey(shellKeyPath, writable: true);
            if (shellKey?.GetValue(PathConstants.RunFenceFallbackValueName) == null)
                return false;

            shellKey.DeleteValue(PathConstants.RunFenceFallbackValueName, throwOnMissingValue: false);
            DeleteEmptySubKey(DirectoryShellPath);
            return true;
        }
        catch (Exception ex)
        {
            onError?.Invoke(DirectoryShellPath, ex);
            return false;
        }
    }

    private void DeleteEmptyParentChain(string relativeSubKeyPath)
    {
        var parentPath = relativeSubKeyPath[..relativeSubKeyPath.LastIndexOf('\\')];
        DeleteEmptySubKey(parentPath);

        if (relativeSubKeyPath.StartsWith(ShellWindowsClsidRelativePath, StringComparison.OrdinalIgnoreCase))
        {
            DeleteEmptySubKey(ShellWindowsClsidRelativePath + @"\shell\open");
            DeleteEmptySubKey(ShellWindowsClsidRelativePath + @"\shell");
            DeleteEmptySubKey(ShellWindowsClsidRelativePath);
            return;
        }

        var shellParentPath = parentPath[..parentPath.LastIndexOf('\\')];
        DeleteEmptySubKey(shellParentPath);
    }

    private void DeleteEmptySubKey(string relativeSubKeyPath)
    {
        try
        {
            var subKeyPath = GetClassesRegistryPath(relativeSubKeyPath);
            using var key = root.OpenSubKey(subKeyPath);
            if (key is { SubKeyCount: 0, ValueCount: 0 })
                root.DeleteSubKey(subKeyPath, throwOnMissingSubKey: false);
        }
        catch
        {
        }
    }

    private string GetClassesRegistryPath(string relativeSubKeyPath)
    {
        return $@"{classesRootPath}\{relativeSubKeyPath}";
    }
}
