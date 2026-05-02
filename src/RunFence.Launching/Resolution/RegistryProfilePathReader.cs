using Microsoft.Win32;
using RunFence.Core;

namespace RunFence.Launching.Resolution;

public sealed class RegistryProfilePathReader : IProfilePathReader
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, string> _profilePaths = new(StringComparer.OrdinalIgnoreCase);

    public string? GetProfilePath(string sid)
    {
        lock (_lock)
        {
            if (_profilePaths.TryGetValue(sid, out var cached))
                return cached;
        }

        var resolved = ReadProfilePath(sid);
        if (resolved != null)
        {
            lock (_lock)
            {
                _profilePaths[sid] = resolved;
            }
        }

        return resolved;
    }

    private static string? ReadProfilePath(string sid)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"{PathConstants.ProfileListRegistryKey}\{sid}");
        var path = key?.GetValue("ProfileImagePath") as string;
        return path != null ? System.Environment.ExpandEnvironmentVariables(path) : null;
    }
}
