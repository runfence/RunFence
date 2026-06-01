namespace RunFence.Account.UI;

public sealed class WindowsTerminalDeploymentDirectoryCleaner
{
    public void TryDeleteIfExists(string directoryPath)
    {
        try
        {
            if (!TryGetAttributes(directoryPath, out var attributes))
                return;

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(directoryPath, recursive: false);
                return;
            }

            if ((attributes & FileAttributes.Directory) == 0)
                return;

            DeleteDirectoryTree(directoryPath);
        }
        catch (Exception)
        {
        }
    }

    private void DeleteDirectoryTree(string directoryPath)
    {
        foreach (var filePath in Directory.EnumerateFiles(directoryPath))
            TryDeleteFile(filePath);

        foreach (var childDirectoryPath in Directory.EnumerateDirectories(directoryPath))
        {
            if (IsReparsePoint(childDirectoryPath))
            {
                TryDeleteDirectory(childDirectoryPath);
                continue;
            }

            DeleteDirectoryTree(childDirectoryPath);
        }

        TryDeleteDirectory(directoryPath);
    }

    private bool IsReparsePoint(string path)
        => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }

        attributes = default;
        return false;
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            File.Delete(filePath);
        }
        catch (Exception)
        {
        }
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        try
        {
            Directory.Delete(directoryPath, recursive: false);
        }
        catch (Exception)
        {
        }
    }
}
