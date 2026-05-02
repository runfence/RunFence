using Microsoft.Win32;
using System.Text;

namespace RunFence.Core.Helpers;

/// <summary>
/// Pure-logic helpers for association command handling: argument substitution,
/// RunFence ProgId detection, and registry fallback restoration.
/// </summary>
/// <remarks>Lines above threshold: 369 lines: all methods are pure string operations in RunFence.Core (no deps). Splitting into "substitution" vs "detection" would separate methods that callers use together (substitute then detect), requiring callers to depend on two classes instead of one for a single operation. Reviewed 2026-04-09.</remarks>
public static class AssociationCommandHelper
{
    /// <summary>
    /// Expands environment variables in <paramref name="command"/>, substitutes supported
    /// association placeholders, and appends <paramref name="rawArguments"/> when no supported
    /// placeholder exists. Unsupported association placeholders throw.
    /// </summary>
    public static string SubstituteArgument(string command, string? rawArguments)
    {
        if (!TrySubstituteArgument(
                command,
                rawArguments,
                out var materializedCommand,
                out _,
                out var rejectionReason))
        {
            throw new InvalidOperationException(rejectionReason);
        }

        return materializedCommand;
    }

    /// <summary>
    /// Pure shared materialization path for association commands used by both RunFence.exe and
    /// RunFence.Launcher.exe. Expands environment variables, handles supported placeholders,
    /// appends the raw argument when needed, validates the command line, and parses the
    /// executable plus remaining arguments.
    /// </summary>
    public static bool TryMaterializeCommand(
        string command,
        string? rawArguments,
        out AssociationCommandMaterialization materialization,
        out string rejectionReason)
    {
        materialization = default!;

        if (!TrySubstituteArgument(
                command,
                rawArguments,
                out var materializedCommand,
                out var usedSupportedPlaceholder,
                out rejectionReason))
        {
            return false;
        }

        if (!HasWellFormedCommandLine(materializedCommand))
        {
            rejectionReason = "materialized command line is malformed";
            return false;
        }

        var exePath = AssociationRegistryCommandParser.ExtractExeFromCommand(materializedCommand);
        if (string.IsNullOrWhiteSpace(exePath))
        {
            rejectionReason = "no executable could be extracted";
            return false;
        }

        materialization = new AssociationCommandMaterialization(
            ExpandedCommand: Environment.ExpandEnvironmentVariables(command),
            MaterializedCommand: materializedCommand,
            ExePath: exePath,
            Arguments: CommandLineHelper.SkipArgs(materializedCommand, 1),
            UsedSupportedPlaceholder: usedSupportedPlaceholder);
        rejectionReason = string.Empty;
        return true;
    }

    public static bool TrySubstituteArgument(
        string command,
        string? rawArguments,
        out string materializedCommand,
        out bool usedSupportedPlaceholder,
        out string rejectionReason)
    {
        var expanded = Environment.ExpandEnvironmentVariables(command);
        var builder = new StringBuilder(expanded.Length + (rawArguments?.Length ?? 0) + 8);
        usedSupportedPlaceholder = false;

        for (var i = 0; i < expanded.Length; i++)
        {
            var c = expanded[i];
            if (c != '%')
            {
                builder.Append(c);
                continue;
            }

            if (TryGetEnvironmentVariableTokenLength(expanded, i, out var envTokenLength))
            {
                builder.Append(expanded, i, envTokenLength);
                i += envTokenLength - 1;
                continue;
            }

            if (TryGetSupportedPlaceholderLength(expanded, i, out var placeholderLength))
            {
                builder.Append(rawArguments ?? string.Empty);
                usedSupportedPlaceholder = true;
                i += placeholderLength - 1;
                continue;
            }

            if (TryGetUnsupportedPlaceholder(expanded, i, out var unsupportedPlaceholder))
            {
                rejectionReason = $"command contains unsupported association placeholder '{unsupportedPlaceholder}'";
                materializedCommand = string.Empty;
                return false;
            }

            builder.Append(c);
        }

        if (!usedSupportedPlaceholder && rawArguments != null)
        {
            if (builder.Length > 0)
                builder.Append(' ');
            builder.Append(CommandLineHelper.JoinArgs([rawArguments]));
        }

        materializedCommand = builder.ToString();
        rejectionReason = string.Empty;
        return true;
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="progId"/> starts with the
    /// RunFence ProgId prefix (e.g., <c>"RunFence_.pdf"</c>, <c>"RunFence_http"</c>).
    /// </summary>
    public static bool IsRunFenceProgId(string? progId) =>
        progId?.StartsWith(PathConstants.HandlerProgIdPrefix, StringComparison.OrdinalIgnoreCase) == true;

    public static bool IsRunFenceLauncherCommand(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return false;

        var exePath = AssociationRegistryCommandParser.ExtractExeFromCommand(commandLine);
        return IsRunFenceLauncherExecutablePath(exePath);
    }

    public static bool IsRunFenceExecutablePath(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return false;

        var exeName = Path.GetFileName(exePath);
        return string.Equals(exeName, PathConstants.LauncherExeName, StringComparison.OrdinalIgnoreCase)
               || string.Equals(exeName, PathConstants.AppName + ".exe", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRunFenceLauncherExecutablePath(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return false;

        var exeName = Path.GetFileName(exePath);
        return string.Equals(exeName, PathConstants.LauncherExeName, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRunFenceAssociationLauncherCommand(string? commandLine)
    {
        return TryParseRunFenceAssociationLauncherCommand(commandLine, out _, out _);
    }

    public static bool TryParseRunFenceAssociationLauncherCommand(
        string? commandLine,
        out string association,
        out string rawArgument)
    {
        association = string.Empty;
        rawArgument = string.Empty;

        if (string.IsNullOrWhiteSpace(commandLine))
            return false;

        var exePath = AssociationRegistryCommandParser.ExtractExeFromCommand(commandLine);
        if (string.IsNullOrWhiteSpace(exePath))
            return false;

        if (!IsRunFenceLauncherExecutablePath(exePath))
            return false;

        var remaining = CommandLineHelper.SkipArgs(commandLine, 1);
        if (string.IsNullOrWhiteSpace(remaining))
            return false;

        var args = CommandLineHelper.SplitArgs(remaining);
        if (args.Length < 3
            || !string.Equals(args[0], "--resolve", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(args[1])
            || string.IsNullOrWhiteSpace(args[2]))
        {
            return false;
        }

        association = args[1];
        rawArgument = args[2];
        return true;
    }

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
        if (associationKey.GetValue(PathConstants.RunFenceFallbackValueName) is not string fallbackValue)
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

        associationKey.DeleteValue(PathConstants.RunFenceFallbackValueName, throwOnMissingValue: false);
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

    private static bool TryGetSupportedPlaceholderLength(string command, int index, out int length)
    {
        length = 0;
        if (index + 1 >= command.Length)
            return false;

        var next = command[index + 1];
        if (next == '*')
        {
            length = 2;
            return true;
        }

        if (next is '1' or 'L' or 'l' or 'V' or 'v' or 'U' or 'u')
        {
            length = 2;
            return true;
        }

        return false;
    }

    private static bool TryGetEnvironmentVariableTokenLength(string command, int index, out int length)
    {
        length = 0;
        if (index + 2 >= command.Length)
            return false;

        var closingIndex = command.IndexOf('%', index + 1);
        if (closingIndex <= index + 1)
            return false;

        for (var i = index + 1; i < closingIndex; i++)
        {
            var c = command[i];
            if (!char.IsLetterOrDigit(c) && c is not '_' and not '(' and not ')')
                return false;
        }

        length = closingIndex - index + 1;
        return true;
    }

    private static bool TryGetUnsupportedPlaceholder(string command, int index, out string placeholder)
    {
        placeholder = string.Empty;
        if (index + 1 >= command.Length)
            return false;

        var next = command[index + 1];
        if (char.IsWhiteSpace(next) || next is '"' or '%')
            return false;

        placeholder = "%" + next;
        return true;
    }

    private static bool HasWellFormedCommandLine(string commandLine)
    {
        var inQuotes = false;

        for (var i = 0; i < commandLine.Length; i++)
        {
            var c = commandLine[i];
            if (c == '\\')
            {
                var backslashStart = i;
                while (i < commandLine.Length && commandLine[i] == '\\')
                    i++;

                var backslashCount = i - backslashStart;
                if (i < commandLine.Length && commandLine[i] == '"')
                {
                    if (backslashCount % 2 == 0)
                        inQuotes = !inQuotes;
                    continue;
                }

                i--;
                continue;
            }

            if (c == '"')
                inQuotes = !inQuotes;
        }

        return !inQuotes;
    }
}
