namespace RunFence.Launch;

public static class FolderHandlerRegistryPathMapper
{
    public static string BuildFullPath(string accountSid, string subKeyPath)
    {
        return string.Equals(subKeyPath, "Software", StringComparison.OrdinalIgnoreCase)
               || subKeyPath.StartsWith(@"Software\", StringComparison.OrdinalIgnoreCase)
            ? $@"{accountSid}\{subKeyPath}"
            : $@"{accountSid}\Software\Classes\{subKeyPath}";
    }

    public static string NormalizeValueName(string? valueName) => valueName ?? string.Empty;

    public static string BuildValueIdentifier(string subKeyPath, string? valueName)
        => $"{subKeyPath}|{NormalizeValueName(valueName)}";
}
