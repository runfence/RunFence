namespace RunFence.Core;

public sealed class AppIdValidator
{
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public bool IsValidAppId(string? appId) => TryGetInvalidReason(appId, out _) == false;

    public void EnsureValidAppId(string? appId, string? context = null)
    {
        if (!TryGetInvalidReason(appId, out var reason))
            return;

        var label = string.IsNullOrWhiteSpace(context) ? "App ID" : context;
        throw new InvalidAppIdException(appId, $"{label} is invalid: {reason}");
    }

    private static bool TryGetInvalidReason(string? appId, out string reason)
    {
        if (appId == null)
        {
            reason = "value is required.";
            return true;
        }

        if (string.IsNullOrWhiteSpace(appId))
        {
            reason = "value cannot be empty or whitespace.";
            return true;
        }

        if (!string.Equals(appId, appId.Trim(), StringComparison.Ordinal))
        {
            reason = "value cannot start or end with whitespace.";
            return true;
        }

        if (appId is "." or "..")
        {
            reason = "relative path segments are not allowed.";
            return true;
        }

        if (appId.Contains(Path.DirectorySeparatorChar) || appId.Contains(Path.AltDirectorySeparatorChar))
        {
            reason = "path separators are not allowed.";
            return true;
        }

        if (appId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            reason = "value contains invalid filename characters.";
            return true;
        }

        var stem = appId.Split('.')[0];
        if (ReservedDeviceNames.Contains(stem))
        {
            reason = "reserved Windows device names are not allowed.";
            return true;
        }

        reason = string.Empty;
        return false;
    }
}
