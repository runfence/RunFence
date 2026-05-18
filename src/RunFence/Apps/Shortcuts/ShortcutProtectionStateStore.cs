using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RunFence.Apps.Shortcuts;

public sealed class ShortcutProtectionStateStore(string rootDirectory) : IShortcutProtectionStateStore
{
    public ShortcutProtectionState? Load(string shortcutPath)
    {
        var path = GetStateFilePath(shortcutPath);
        if (!File.Exists(path))
            return null;

        var json = File.ReadAllText(path);
        var state = JsonSerializer.Deserialize<ShortcutProtectionState>(json);
        if (state is not { ShortcutPath.Length: > 0 })
            throw new InvalidDataException($"Invalid shortcut protection state for '{shortcutPath}'.");

        if (!string.Equals(NormalizePath(state.ShortcutPath), NormalizePath(shortcutPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Shortcut protection state path mismatch for '{shortcutPath}'.");

        return state;
    }

    public void Save(ShortcutProtectionState state)
    {
        Directory.CreateDirectory(rootDirectory);
        var path = GetStateFilePath(state.ShortcutPath);
        File.WriteAllText(path, JsonSerializer.Serialize(state));
    }

    public void Delete(string shortcutPath)
    {
        var path = GetStateFilePath(shortcutPath);
        if (File.Exists(path))
            File.Delete(path);
    }

    private string GetStateFilePath(string shortcutPath)
    {
        var normalizedPath = NormalizePath(shortcutPath);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        var fileName = Convert.ToHexString(hashBytes) + ".json";
        return Path.Combine(rootDirectory, fileName);
    }

    private static string NormalizePath(string shortcutPath)
        => Path.GetFullPath(shortcutPath).ToUpperInvariant();
}
