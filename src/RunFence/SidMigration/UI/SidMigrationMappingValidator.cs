using System.Security.Principal;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.SidMigration.UI;

/// <summary>
/// Validates SID migration mappings and resolves SID display names.
/// Separates validation and resolution domain logic from the grid wiring in
/// <see cref="SidMigrationMappingBuilder"/>.
/// </summary>
public class SidMigrationMappingValidator(IProfilePathResolver profilePathResolver)
{
    /// <summary>
    /// Checks for duplicate <see cref="SidMigrationMapping.NewSid"/> values across mappings.
    /// Returns the set of new SIDs that appear more than once, or an empty set when all are unique.
    /// </summary>
    public HashSet<string> FindDuplicateNewSids(IReadOnlyList<SidMigrationMapping> mappings)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in mappings)
        {
            if (!seen.Add(mapping.NewSid))
                duplicates.Add(mapping.NewSid);
        }
        return duplicates;
    }

    /// <summary>
    /// Resolves a display name for a SID by looking up the profile registry.
    /// Returns a formatted string such as "Username (S-1-5-...)", or null when no name is found.
    /// </summary>
    public string? ResolveSidName(string sid)
    {
        var regName = profilePathResolver.TryResolveNameFromRegistry(sid);
        return regName != null ? $"{regName} ({sid})" : null;
    }

    /// <summary>
    /// Tries to parse <paramref name="sidString"/> as a <see cref="SecurityIdentifier"/>.
    /// Returns the canonical SID value string on success.
    /// </summary>
    public static bool TryParseSid(string sidString, out string canonicalSid)
    {
        sidString = sidString.Trim();
        try
        {
            var sid = new SecurityIdentifier(sidString);
            canonicalSid = sid.Value;
            return true;
        }
        catch
        {
            canonicalSid = sidString;
            return false;
        }
    }

    public static bool TryResolveSidInput(
        string input,
        IReadOnlyDictionary<string, string> sidDisplayNames,
        out string canonicalSid)
    {
        input = input.Trim();
        foreach (var (sid, displayName) in sidDisplayNames)
        {
            if (string.Equals(input, displayName, StringComparison.OrdinalIgnoreCase))
            {
                canonicalSid = sid;
                return true;
            }
        }

        if (input.EndsWith(')'))
        {
            var openingParen = input.LastIndexOf('(');
            if (openingParen >= 0)
            {
                var parenthesizedSid = input[(openingParen + 1)..^1];
                if (TryParseSid(parenthesizedSid, out canonicalSid))
                    return true;
            }
        }

        return TryParseSid(input, out canonicalSid);
    }
}
