using System.Diagnostics;
using Microsoft.Win32;
using RunFence.Core;
using RunFence.Core.Helpers;

namespace RunFence.Launcher;

/// <summary>
/// Resolves and launches fallback handlers when RunFence denies access or does not
/// recognise an association. Delegates pure command logic to <see cref="AssociationCommandHelper"/>.
/// </summary>
public static class AssociationFallbackHelper
{
    /// <summary>
    /// Silently falls back to the original handler stored in <c>RunFenceFallback</c>,
    /// or to the HKLM handler when no fallback is stored.
    /// Used when the HKCU override should be kept in place (e.g. <c>PathPrefixMismatch</c> —
    /// the association is still valid for other paths).
    /// </summary>
    public static int LaunchFallback(string association, string? rawArguments)
    {
        string? fallbackValue;
        using (var assocKey = Registry.ClassesRoot.OpenSubKey(association))
            fallbackValue = assocKey?.GetValue(PathConstants.RunFenceFallbackValueName) as string;

        string? command;
        command = !string.IsNullOrEmpty(fallbackValue) ? ResolveHandler(fallbackValue) : LookupHklmHandler(association);

        if (command != null)
            return LaunchResolvedCommand(command, rawArguments);

        LauncherIpcHelper.ShowError("No fallback handler found for '" + association + "'.");
        return 1;
    }

    /// <summary>
    /// Restores the original handler in HKCU (removing the RunFence override) and then
    /// launches the original handler. Used when the HKCU override should be removed
    /// (e.g. <c>AccessDenied</c> — caller is no longer authorized; <c>UnknownAssociation</c> — association no longer registered).
    /// </summary>
    public static int CleanupAndLaunchFallback(string association, string? rawArguments)
    {
        string? fallbackValue = null;

        using (var assocKey = Registry.CurrentUser.OpenSubKey(
                   @"Software\Classes\" + association, writable: true))
        {
            if (assocKey != null)
            {
                fallbackValue = AssociationCommandHelper.RestoreFromFallback(assocKey, association);
                OpenFolderNative.SHChangeNotify(
                    OpenFolderNative.SHCNE_ASSOCCHANGED,
                    OpenFolderNative.SHCNF_IDLIST,
                    IntPtr.Zero,
                    IntPtr.Zero);
            }
        }

        string? command;
        command = !string.IsNullOrEmpty(fallbackValue) ? ResolveHandler(fallbackValue) : LookupHklmHandler(association);

        if (command != null)
            return LaunchResolvedCommand(command, rawArguments);

        LauncherIpcHelper.ShowError("No fallback handler found for '" + association + "'.");
        return 1;
    }

    /// <summary>
    /// Resolves <paramref name="value"/> to a command string.
    /// Tries ProgId lookup first; treats as a direct command string if that fails.
    /// Returns <see langword="null"/> if the value is a RunFence ProgId (avoid re-invocation loop).
    /// </summary>
    private static string? ResolveHandler(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        if (AssociationCommandHelper.IsRunFenceProgId(value))
            return null;

        using var commandKey = Registry.ClassesRoot.OpenSubKey(value + @"\shell\open\command");
        if (commandKey != null)
        {
            var command = commandKey.GetValue(null) as string;
            if (!string.IsNullOrEmpty(command))
                return command;
        }

        return value;
    }

    /// <summary>
    /// Looks up the handler command from HKLM for the given association.
    /// For extensions (starting with <c>.</c>), reads the default ProgId and resolves it.
    /// For protocols, reads <c>shell\open\command</c> directly.
    /// </summary>
    private static string? LookupHklmHandler(string association)
    {
        using var hklmKey = Registry.LocalMachine.OpenSubKey(@"Software\Classes\" + association);
        if (hklmKey == null)
            return null;

        if (association.StartsWith('.'))
        {
            var progId = hklmKey.GetValue(null) as string;
            if (AssociationCommandHelper.IsRunFenceProgId(progId))
                return null;
            return ResolveHandlerFromHklm(progId);
        }

        using var commandKey = hklmKey.OpenSubKey(@"shell\open\command");
        return commandKey?.GetValue(null) as string;
    }

    private static string? ResolveHandlerFromHklm(string? progId)
    {
        if (string.IsNullOrEmpty(progId))
            return null;

        using var commandKey = Registry.LocalMachine.OpenSubKey(
            @"Software\Classes\" + progId + @"\shell\open\command");
        if (commandKey != null)
        {
            var command = commandKey.GetValue(null) as string;
            if (!string.IsNullOrEmpty(command))
                return command;
        }

        return null;
    }

    /// <summary>
    /// Substitutes <paramref name="rawArguments"/> into <paramref name="command"/> and
    /// launches via <see cref="Process.Start"/> with <c>UseShellExecute = true</c>.
    /// The command is parsed into executable and arguments using
    /// <c>CommandLineToArgvW</c>-compatible rules so quoted paths with spaces work correctly.
    /// </summary>
    private static int LaunchResolvedCommand(string command, string? rawArguments)
    {
        try
        {
            if (!TryBuildProcessStartInfo(command, rawArguments, out var startInfo, out var errorMessage))
            {
                LauncherIpcHelper.ShowError(errorMessage);
                return 1;
            }

            Process.Start(startInfo);
            return 0;
        }
        catch (Exception ex)
        {
            LauncherIpcHelper.ShowError("Failed to launch fallback handler: " + ex.Message);
            return 1;
        }
    }

    public static bool TryBuildProcessStartInfo(
        string command,
        string? rawArguments,
        out ProcessStartInfo processStartInfo,
        out string errorMessage)
    {
        processStartInfo = new ProcessStartInfo();

        if (!AssociationCommandHelper.TryMaterializeCommand(
                command,
                rawArguments,
                out var materialization,
                out var rejectionReason))
        {
            errorMessage = "Failed to launch fallback handler: " + rejectionReason;
            return false;
        }

        processStartInfo = new ProcessStartInfo
        {
            FileName = materialization.ExePath,
            Arguments = materialization.Arguments ?? string.Empty,
            UseShellExecute = true
        };
        errorMessage = string.Empty;
        return true;
    }
}
