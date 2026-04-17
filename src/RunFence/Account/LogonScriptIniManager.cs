using System.Text;
using System.Text.RegularExpressions;

namespace RunFence.Account;

/// <summary>
/// Manages INI file reading and writing for per-user MLGPO logon scripts
/// (scripts.ini) and the associated gpt.ini version file.
/// Extracted from <see cref="GroupPolicyScriptHelper"/> to isolate the INI
/// manipulation logic from the higher-level script registration flow.
/// </summary>
public partial class LogonScriptIniManager
{
    // Scripts Client-Side Extension GUID + Scripts snap-in tool GUID.
    // Required in gpt.ini for Group Policy to apply logon scripts.
    private const string ScriptsExtensionNames =
        "[{42B5FAAE-6536-11D2-AE5A-0000F87571E3}{40B66650-4972-11D1-A7CA-0000F87571E3}]";

    // --- Source-generated regex patterns ---

    [GeneratedRegex(@"^(\d+)CmdLine=")]
    private static partial Regex CmdLineIndexRegex();

    [GeneratedRegex(@"^\d+CmdLine=(.+)$")]
    public static partial Regex CmdLineValueRegex();

    [GeneratedRegex(@"^\d+Parameters=(.*)$")]
    private static partial Regex ParametersValueRegex();

    /// <summary>
    /// Appends a logon entry (CmdLine + Parameters pair) to the [Logon] section of scripts.ini.
    /// Creates the file and directory if they do not exist.
    /// </summary>
    public void AppendLogonEntry(string iniPath, string cmdLine)
    {
        var lines = File.Exists(iniPath) ? new List<string>(File.ReadAllLines(iniPath)) : new List<string>();

        var sectionIdx = lines.FindIndex(l => l.Trim().Equals("[Logon]", StringComparison.OrdinalIgnoreCase));
        if (sectionIdx < 0)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                lines.Add("");
            lines.Add("[Logon]");
            sectionIdx = lines.Count - 1;
        }

        var nextIndex = GetNextLogonIndex(lines, sectionIdx);

        // Find end of [Logon] section
        var insertAt = sectionIdx + 1;
        while (insertAt < lines.Count)
        {
            var l = lines[insertAt].TrimStart();
            if (l.StartsWith('[') && l.Contains(']'))
                break;
            insertAt++;
        }

        lines.Insert(insertAt, $"{nextIndex}CmdLine={cmdLine}");
        lines.Insert(insertAt + 1, $"{nextIndex}Parameters=");

        Directory.CreateDirectory(Path.GetDirectoryName(iniPath)!);
        WriteIniAtomic(iniPath, lines);
    }

    /// <summary>
    /// Removes the logon entry for <paramref name="wrapperPath"/> (and optionally
    /// <paramref name="legacyWrapperPath"/>) from scripts.ini.
    /// Deletes the file and cleans up empty ancestor directories when no entries remain.
    /// </summary>
    public void RemoveLogonEntry(string iniPath, string wrapperPath, string? legacyWrapperPath = null)
    {
        var lines = File.ReadAllLines(iniPath);
        var result = new List<string>();
        var inLogon = false;
        var entries = new List<(string cmdLine, string parameters)>();
        string? pendingCmd = null;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('['))
            {
                if (inLogon)
                    FlushEntries(result, entries);
                inLogon = trimmed.Equals("[Logon]", StringComparison.OrdinalIgnoreCase);
                result.Add(line);
                continue;
            }

            if (!inLogon)
            {
                result.Add(line);
                continue;
            }

            var cmdMatch = CmdLineValueRegex().Match(trimmed);
            var paramMatch = ParametersValueRegex().Match(trimmed);

            if (cmdMatch.Success)
            {
                // Flush orphaned entry: a CmdLine without a following Parameters line
                if (pendingCmd != null && !IsLogoffEntry(pendingCmd, wrapperPath, legacyWrapperPath))
                    entries.Add((pendingCmd, ""));
                pendingCmd = cmdMatch.Groups[1].Value;
            }
            else if (paramMatch.Success && pendingCmd != null)
            {
                // Remove both old-style (logoff.exe) and new-style (wrapper script)
                if (!IsLogoffEntry(pendingCmd, wrapperPath, legacyWrapperPath))
                    entries.Add((pendingCmd, paramMatch.Groups[1].Value));
                pendingCmd = null;
            }
            else
            {
                result.Add(line);
            }
        }

        // Handle trailing CmdLine without a Parameters line (truncated file)
        if (pendingCmd != null && !IsLogoffEntry(pendingCmd, wrapperPath, legacyWrapperPath))
            entries.Add((pendingCmd, ""));

        if (inLogon)
            FlushEntries(result, entries);

        // If only section headers and whitespace remain, delete the file and clean up
        if (result.All(l => string.IsNullOrWhiteSpace(l) || l.TrimStart().StartsWith('[')))
        {
            try
            {
                File.Delete(iniPath);
            }
            catch
            {
            }

            CleanupEmptyDirs(iniPath);
        }
        else
        {
            WriteIniAtomic(iniPath, result);
        }
    }

    /// <summary>
    /// Creates or updates gpt.ini for the per-user MLGPO so Group Policy notices
    /// the scripts.ini change and applies it on next logon. The Version counter
    /// must change (+65536, user version upper 16 bits) for GP to re-process.
    /// When hasScripts is false, gpt.ini is removed so the SID directory can be
    /// fully cleaned up.
    /// </summary>
    public void UpdateGptIni(string gptPath, bool hasScripts)
    {
        if (!hasScripts)
        {
            try
            {
                if (File.Exists(gptPath))
                    File.Delete(gptPath);
            }
            catch
            {
            }

            // Clean up the SID directory if now empty (scripts.ini already gone)
            try
            {
                var sidDir = Path.GetDirectoryName(gptPath)!;
                if (Directory.Exists(sidDir) && !Directory.EnumerateFileSystemEntries(sidDir).Any())
                    Directory.Delete(sidDir);
            }
            catch
            {
            }

            return;
        }

        // Read existing version so we always produce a new value (GP re-processes on change).
        int version = 0;
        if (File.Exists(gptPath))
        {
            foreach (var line in File.ReadAllLines(gptPath))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("Version=", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(trimmed["Version=".Length..], out var v))
                {
                    version = v;
                    break;
                }
            }
        }

        // Increment user version (upper 16 bits, +65536) per [MS-GPOL]: upper 16 bits = user
        // version, lower 16 bits = machine version. Per-user MLGPOs contain only user settings.
        // MMC confirms: writes 65536, 131072, 196608, ... (user version +1 per edit, machine = 0).
        // Overflow is harmless: the value wraps around and Windows still detects a change
        // (any difference from the cached version triggers re-processing).
        version = (int)((uint)version + 65536u);

        var lines = new[]
        {
            "[General]",
            $"gPCUserExtensionNames={ScriptsExtensionNames}",
            $"Version={version}"
        };

        var dir = Path.GetDirectoryName(gptPath)!;
        Directory.CreateDirectory(dir);
        // Mark Hidden+System to match the attributes MMC sets on per-user MLGPO directories.
        try
        {
            new DirectoryInfo(dir).Attributes |= FileAttributes.Hidden | FileAttributes.System;
        }
        catch
        {
        }

        var tmpPath = Path.Combine(dir, "gpt.ini." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        // gpt.ini uses ASCII-compatible encoding without BOM; a BOM prefix would break
        // the INI section parser. scripts.ini is different and requires UTF-16 LE BOM.
        File.WriteAllLines(tmpPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        if (File.Exists(gptPath))
            File.Replace(tmpPath, gptPath, gptPath + ".bak");
        else
            File.Move(tmpPath, gptPath);
    }

    private static bool IsLogoffEntry(string cmdLine, string wrapperPath, string? legacyWrapperPath) =>
        cmdLine.Equals("logoff.exe", StringComparison.OrdinalIgnoreCase) ||
        cmdLine.Equals(wrapperPath, StringComparison.OrdinalIgnoreCase) ||
        (legacyWrapperPath != null && cmdLine.Equals(legacyWrapperPath, StringComparison.OrdinalIgnoreCase));

    private static int GetNextLogonIndex(List<string> lines, int sectionIdx)
    {
        int maxIdx = -1;
        for (int i = sectionIdx + 1; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('['))
                break;

            var m = CmdLineIndexRegex().Match(trimmed);
            if (m.Success)
            {
                var idx = int.Parse(m.Groups[1].Value);
                if (idx > maxIdx)
                    maxIdx = idx;
            }
        }

        return maxIdx + 1;
    }

    private static void FlushEntries(List<string> result, List<(string cmdLine, string parameters)> entries)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            result.Add($"{i}CmdLine={entries[i].cmdLine}");
            result.Add($"{i}Parameters={entries[i].parameters}");
        }

        entries.Clear();
    }

    private static void WriteIniAtomic(string targetPath, IEnumerable<string> lines)
    {
        var dir = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(dir);
        var tmpPath = Path.Combine(dir, Path.GetFileName(targetPath) + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        // scripts.ini must be UTF-16 LE with BOM for Group Policy to read it
        File.WriteAllLines(tmpPath, lines, new UnicodeEncoding(bigEndian: false, byteOrderMark: true));
        if (File.Exists(targetPath))
            File.Replace(tmpPath, targetPath, targetPath + ".bak");
        else
            File.Move(tmpPath, targetPath);
    }

    private static void CleanupEmptyDirs(string iniPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(iniPath)!;
            // Walk up: Scripts → User → <SID> — delete each if empty
            for (int i = 0; i < 3; i++)
            {
                if (!Directory.Exists(dir))
                    break;
                if (Directory.EnumerateFileSystemEntries(dir).Any())
                    break;
                Directory.Delete(dir);
                dir = Path.GetDirectoryName(dir)!;
            }
        }
        catch
        {
        }
    }
}