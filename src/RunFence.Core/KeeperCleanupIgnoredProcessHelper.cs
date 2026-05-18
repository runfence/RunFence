namespace RunFence.Core;

public static class KeeperCleanupIgnoredProcessHelper
{
    public static bool IsIgnoredProcess(string? imagePathOrName)
    {
        if (string.IsNullOrWhiteSpace(imagePathOrName))
            return false;

        string fileName;
        try
        {
            fileName = Path.GetFileName(imagePathOrName);
        }
        catch
        {
            fileName = imagePathOrName;
        }

        return string.Equals(fileName, "conhost.exe", StringComparison.OrdinalIgnoreCase);
    }
}
