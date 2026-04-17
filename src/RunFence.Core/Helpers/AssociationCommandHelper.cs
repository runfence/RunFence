using Microsoft.Win32;

namespace RunFence.Core.Helpers;

/// <summary>
/// Pure-logic helpers for association command handling: argument substitution,
/// RunFence ProgId detection, and registry fallback restoration.
/// </summary>
public static class AssociationCommandHelper
{
    /// <summary>
    /// Expands environment variables in <paramref name="command"/>, then substitutes
    /// <paramref name="rawArguments"/> for the first <c>%1</c> placeholder.
    /// If no <c>%1</c> is present and <paramref name="rawArguments"/> is non-null,
    /// the arguments are appended with a leading space.
    /// </summary>
    public static string SubstituteArgument(string command, string? rawArguments)
    {
        var expanded = Environment.ExpandEnvironmentVariables(command);

        var placeholder = expanded.IndexOf("%1", StringComparison.Ordinal);
        if (placeholder >= 0)
            return expanded[..placeholder] + (rawArguments ?? string.Empty) + expanded[(placeholder + 2)..];

        if (rawArguments != null)
            return expanded + " " + rawArguments;

        return expanded;
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="progId"/> starts with the
    /// RunFence ProgId prefix (e.g., <c>"RunFence_.pdf"</c>, <c>"RunFence_http"</c>).
    /// </summary>
    public static bool IsRunFenceProgId(string? progId) =>
        progId?.StartsWith(Constants.HandlerProgIdPrefix, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Restores the original handler for the association represented by
    /// <paramref name="associationKey"/> (already opened writable at
    /// <c>Software\Classes\{keyName}</c>) and removes the <c>RunFenceFallback</c> marker.
    /// </summary>
    /// <param name="associationKey">Writable registry key at <c>Software\Classes\{keyName}</c>.</param>
    /// <param name="keyName">Association key name (e.g., <c>".pdf"</c>, <c>"mailto"</c>).</param>
    /// <returns>
    /// The <c>RunFenceFallback</c> value that was stored (may be an empty string meaning
    /// no previous handler existed), or <see langword="null"/> if no fallback was stored.
    /// </returns>
    public static string? RestoreFromFallback(RegistryKey associationKey, string keyName)
    {
        var fallbackValue = associationKey.GetValue(Constants.RunFenceFallbackValueName) as string;
        if (fallbackValue == null)
            return null;

        if (keyName.StartsWith('.'))
        {
            if (!string.IsNullOrEmpty(fallbackValue))
                associationKey.SetValue(null, fallbackValue);
            else
                associationKey.DeleteValue(string.Empty, throwOnMissingValue: false);
            // Clean up shell\open\command added by a command-based direct handler (if any)
            DeleteExtensionCommandSubkeys(associationKey);
        }
        else
        {
            if (!string.IsNullOrEmpty(fallbackValue))
            {
                using var commandKey = associationKey.OpenSubKey(@"shell\open\command", writable: true)
                    ?? associationKey.CreateSubKey(@"shell\open\command");
                commandKey.SetValue(null, fallbackValue);
            }
            else
            {
                associationKey.DeleteSubKeyTree("shell", throwOnMissingSubKey: false);
                associationKey.DeleteValue("URL Protocol", throwOnMissingValue: false);
            }
        }

        associationKey.DeleteValue(Constants.RunFenceFallbackValueName, throwOnMissingValue: false);
        return fallbackValue;
    }

    /// <summary>
    /// Deletes <c>shell\open\command</c> from an extension key and cleans up any empty parent
    /// keys (<c>shell\open</c>, <c>shell</c>), preserving siblings that still have content.
    /// </summary>
    private static void DeleteExtensionCommandSubkeys(RegistryKey extensionKey)
    {
        using (var openKey = extensionKey.OpenSubKey(@"shell\open", writable: true))
            openKey?.DeleteSubKeyTree("command", throwOnMissingSubKey: false);

        using (var openKeyRead = extensionKey.OpenSubKey(@"shell\open"))
        {
            if (openKeyRead is { SubKeyCount: 0, ValueCount: 0 })
            {
                using var shellKey = extensionKey.OpenSubKey("shell", writable: true);
                shellKey?.DeleteSubKey("open", throwOnMissingSubKey: false);
            }
        }

        using (var shellKeyRead = extensionKey.OpenSubKey("shell"))
        {
            if (shellKeyRead is { SubKeyCount: 0, ValueCount: 0 })
                extensionKey.DeleteSubKey("shell", throwOnMissingSubKey: false);
        }
    }
}
