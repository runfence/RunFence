using System.Text;
using RunFence.Core.Models;

namespace RunFence.Firewall.UI;

/// <summary>
/// Handles file-level import and export for the firewall allowlist:
/// reads files into structured import results and writes entries to files.
/// Contains no tab-handler references; callers apply the results to the appropriate handlers.
/// </summary>
public class FirewallAllowlistImportExportService
{
    /// <summary>
    /// Reads the file at <paramref name="path"/> and returns its lines split by type
    /// (allowlist lines vs. localhost port lines). Returns null on file read failure.
    /// </summary>
    public FileImportResult? ImportFromFile(string path)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(path, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            return new FileImportResult(null, ex.Message);
        }

        var allowlistLines = new List<string>();
        var portLines = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (line.StartsWith("localhost:", StringComparison.OrdinalIgnoreCase))
                portLines.Add(line);
            else
                allowlistLines.Add(line);
        }

        return new FileImportResult(new ParsedImportLines(allowlistLines, portLines), null);
    }

    /// <summary>
    /// Exports <paramref name="entries"/> to the file at <paramref name="path"/>.
    /// Returns null on success, or an error message on failure.
    /// </summary>
    public string? ExportToFile(string path, IReadOnlyList<FirewallAllowlistEntry> entries)
    {
        try
        {
            File.WriteAllLines(path, entries.Select(e => e.Value), Encoding.UTF8);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Exports all entries and port exceptions combined to the file at <paramref name="path"/>.
    /// Returns null on success, or an error message on failure.
    /// </summary>
    public string? ExportCombinedToFile(
        string path,
        IReadOnlyList<FirewallAllowlistEntry> entries,
        IReadOnlyList<string> portEntries)
    {
        var lines = entries.Select(e => e.Value)
            .Concat(portEntries.Select(p => $"localhost:{p}"))
            .ToList();
        try
        {
            File.WriteAllLines(path, lines, Encoding.UTF8);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}

/// <summary>
/// Result of reading an import file. <see cref="Lines"/> is non-null on success;
/// <see cref="ErrorMessage"/> is non-null on failure.
/// </summary>
public record FileImportResult(ParsedImportLines? Lines, string? ErrorMessage);

/// <summary>
/// Parsed lines from a firewall settings import file, split by type.
/// </summary>
public record ParsedImportLines(
    IReadOnlyList<string> AllowlistLines,
    IReadOnlyList<string> PortLines);
