using System;
using System.Collections.Generic;
using System.IO;
using RunFence.Launch;

namespace RunFence.Apps.UI;

public readonly record struct HandlerPathSuggestion(string ReplacementPath, string PromptText);

public sealed class AppEntryHandlerPathSuggestionService(
    IHandlerCommandTargetReader targetReader,
    IHandlerPathIconProbe iconProbe)
{
    private const string PromptMessageTemplate = """
        The selected file was launched from:
        {0}

        A registered handler target in the same folder was found:
        {1}

        This may work more correctly when you add file or URL associations.

        Note: trusted auto-repair paths in Program Files and matching account profile locations are repaired automatically;
        other versioned locations are suggested only while editing app entries.

        Replace with the handler target path?
        """;

    public bool TrySuggest(string selectedPath, string? targetAccountSid, out HandlerPathSuggestion suggestion)
    {
        suggestion = default;

        var selectedExtension = Path.GetExtension(selectedPath);
        if (!LaunchFileExtensionRules.IsSupportedHandlerSuggestionExtension(selectedExtension))
            return false;

        var selectedFolder = Path.GetDirectoryName(selectedPath) ?? string.Empty;
        if (selectedFolder.Length == 0)
            return false;

        var selectedIconPresence = iconProbe.GetIconPresence(selectedPath);

        var candidatePaths = CollectCandidateTargets(
                selectedPath,
                selectedExtension,
                selectedFolder,
                selectedIconPresence,
                targetAccountSid)
            .ToList();
        if (candidatePaths.Count != 1)
            return false;

        var replacementPath = candidatePaths[0];
        suggestion = new HandlerPathSuggestion(
            ReplacementPath: replacementPath,
            PromptText: string.Format(PromptMessageTemplate, selectedPath, replacementPath));

        return true;
    }

    private IReadOnlyList<string> CollectCandidateTargets(
        string selectedPath,
        string selectedExtension,
        string selectedFolder,
        HandlerPathIconPresence selectedIconPresence,
        string? targetAccountSid)
    {
        if (!TryGetNormalizedPath(selectedPath, out var normalizedSelected))
            return [];

        if (!TryGetNormalizedPath(selectedFolder, out var normalizedFolder) || normalizedFolder.Length == 0)
            return [];

        var candidatePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targetReader.ReadTargets(targetAccountSid))
        {
            if (!TryGetNormalizedPath(target.ResolvedPath, out var normalizedTargetPath))
                continue;

            if (!seen.Add(normalizedTargetPath))
                continue;

            if (!string.Equals(Path.GetExtension(target.ResolvedPath), selectedExtension, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.Equals(normalizedFolder, normalizedTargetPath, StringComparison.OrdinalIgnoreCase)
                && !normalizedTargetPath.StartsWith(normalizedFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !normalizedTargetPath.StartsWith(normalizedFolder + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue;

            HandlerPathIconPresence targetIconPresence;
            var sameExecutableName = string.Equals(
                Path.GetFileName(selectedPath),
                Path.GetFileName(target.ResolvedPath),
                StringComparison.OrdinalIgnoreCase);

            if (sameExecutableName)
            {
                if (!string.Equals(normalizedSelected, normalizedTargetPath, StringComparison.OrdinalIgnoreCase))
                    candidatePaths.TryAdd(normalizedTargetPath, target.ResolvedPath);

                continue;
            }

            if (selectedIconPresence == HandlerPathIconPresence.Unknown)
                continue;

            if (target.HasExplicitDefaultIcon)
            {
                if (string.IsNullOrWhiteSpace(target.ResolvedDefaultIconPath))
                {
                    targetIconPresence = HandlerPathIconPresence.Unknown;
                }
                else
                {
                    try
                    {
                        targetIconPresence = iconProbe.GetIconPresence(target.ResolvedDefaultIconPath);
                    }
                    catch
                    {
                        targetIconPresence = HandlerPathIconPresence.Unknown;
                    }
                }
            }
            else
            {
                targetIconPresence = iconProbe.GetIconPresence(target.ResolvedPath);
            }

            if (targetIconPresence == HandlerPathIconPresence.Unknown || targetIconPresence != selectedIconPresence)
                continue;

            if (string.Equals(target.ResolvedPath, selectedPath, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.Equals(normalizedSelected, normalizedTargetPath, StringComparison.OrdinalIgnoreCase))
                candidatePaths.TryAdd(normalizedTargetPath, target.ResolvedPath);
        }

        return candidatePaths.Values.ToList();
    }

    private static bool TryGetNormalizedPath(string rawPath, out string normalizedPath)
    {
        try
        {
            normalizedPath = Path.GetFullPath(rawPath);
            return true;
        }
        catch
        {
            normalizedPath = string.Empty;
            return false;
        }
    }
}
