using RunFence.Core.Models;

namespace RunFence.Core;

/// <summary>
/// Stateless SID name resolution utilities: string parsing, display name fallback chain,
/// domain/username extraction, and database name helpers.
/// All methods are pure — they take all inputs as parameters and have no side effects.
/// </summary>
public static class SidNameResolver
{
    public static string ExtractUsername(string resolvedName)
    {
        var backslashIndex = resolvedName.IndexOf('\\');
        return backslashIndex >= 0 ? resolvedName[(backslashIndex + 1)..] : resolvedName;
    }

    /// <summary>
    /// Strips the prefix from a resolved name only if it is the current machine name.
    /// Domain prefixes are preserved. Used in the display layer to avoid showing the
    /// local machine name in UI (e.g., "MYPC\alice" → "alice", "DOMAIN\alice" stays).
    /// </summary>
    public static string StripLocalMachinePrefix(string resolvedName)
    {
        var backslashIndex = resolvedName.IndexOf('\\');
        if (backslashIndex < 0)
            return resolvedName;
        var prefix = resolvedName[..backslashIndex];
        return string.Equals(prefix, Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            ? resolvedName[(backslashIndex + 1)..]
            : resolvedName;
    }

    public static string ExtractDomain(string resolvedName)
    {
        var backslashIndex = resolvedName.IndexOf('\\');
        if (backslashIndex < 0)
            return string.Empty;

        var domain = resolvedName[..backslashIndex];
        // Local accounts: use "." to explicitly target local machine account database
        // (avoids ambiguity when lpDomain=NULL is passed to CreateProcessWithLogonW)
        if (string.Equals(domain, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            return ".";
        return domain;
    }

    public static int DeterministicHash(string sid)
    {
        // FNV-1a hash — stable across processes (unlike string.GetHashCode in .NET Core)
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in sid)
            {
                hash ^= c;
                hash *= 16777619;
            }

            return (int)hash;
        }
    }

    /// <summary>
    /// Appends "(current)" or "(interactive)" suffix to a display name when applicable.
    /// Returns the original name unchanged when neither flag is set.
    /// </summary>
    public static string ApplyAccountSuffix(string displayName, bool isCurrentAccount, bool isInteractiveUser)
    {
        if (isCurrentAccount)
            return $"{displayName} (current)";
        if (isInteractiveUser)
            return $"{displayName} (interactive)";
        return displayName;
    }

    public static string GetDisplayName(
        CredentialEntry cred, ISidResolver sidResolver, IReadOnlyDictionary<string, string>? sidNames)
    {
        var preResolvedName = sidResolver.TryResolveName(cred.Sid);
        if (cred.IsCurrentAccount)
        {
            var name = preResolvedName != null
                ? ExtractUsername(preResolvedName)
                : GetMapFallback(cred.Sid, sidNames) ?? cred.Sid;
            return $"{name} (current)";
        }

        if (cred.IsInteractiveUser)
        {
            var name = preResolvedName != null
                ? ExtractUsername(preResolvedName)
                : GetMapFallback(cred.Sid, sidNames) ?? cred.Sid;
            return $"{name} (interactive)";
        }

        return GetDisplayName(cred.Sid, preResolvedName, sidResolver, sidNames);
    }

    /// <summary>
    /// Resolves a display name for an arbitrary SID using the standard fallback chain:
    /// 1. Pre-resolved live name  2. Registry profile path  3. Central SidNames map  4. Raw SID
    /// </summary>
    public static string GetDisplayName(
        string sid, string? preResolvedName, ISidResolver sidResolver, IReadOnlyDictionary<string, string>? sidNames)
    {
        if (preResolvedName != null)
            return ExtractUsername(preResolvedName);

        // Fall back to registry profile path leaf
        var registryName = sidResolver.TryResolveNameFromRegistry(sid);
        if (registryName != null)
            return registryName;

        var mapName = GetMapFallback(sid, sidNames);
        if (mapName != null)
            return StripLocalMachinePrefix(mapName);

        return sid;
    }

    /// <summary>
    /// Resolves a SID to a bare username string using the standard fallback chain:
    /// 1. Live Windows resolution via <paramref name="sidResolver"/>
    /// 2. Central SidNames map
    /// Returns null if no source provides a name.
    /// </summary>
    public static string? ResolveUsername(string sid, ISidResolver sidResolver, IReadOnlyDictionary<string, string>? sidNames)
    {
        var resolved = sidResolver.TryResolveName(sid);
        if (resolved != null)
            return ExtractUsername(resolved);
        var mapName = GetMapFallback(sid, sidNames);
        return mapName != null ? ExtractUsername(mapName) : null;
    }

    /// <summary>
    /// Resolves a SID to (domain, username) for process launch.
    /// Fallback chain: live resolution → SidNames map → Environment.UserName (current account only).
    /// </summary>
    public static (string Domain, string Username) ResolveDomainAndUsername(
        string sid, bool isCurrentAccount, ISidResolver sidResolver, IReadOnlyDictionary<string, string>? sidNames)
    {
        if (isCurrentAccount)
            return (string.Empty, Environment.UserName);

        var resolved = sidResolver.TryResolveName(sid);
        if (resolved != null)
            return (ExtractDomain(resolved), ExtractUsername(resolved));

        var mapName = GetMapFallback(sid, sidNames);
        if (mapName != null)
            return (ExtractDomain(mapName), ExtractUsername(mapName));

        return (string.Empty, sid);
    }

    /// <summary>
    /// Resolves domain and username for a credential, taking interactive user status into account.
    /// Interactive users are resolved via live lookup (not Environment.UserName).
    /// </summary>
    public static (string Domain, string Username) ResolveDomainAndUsername(
        CredentialEntry cred, ISidResolver sidResolver, IReadOnlyDictionary<string, string>? sidNames)
    {
        return ResolveDomainAndUsername(cred.Sid, cred.IsCurrentAccount, sidResolver, sidNames);
    }

    /// <summary>
    /// Resolves a SID to (domain, username) with an explicit fallback username.
    /// Used during credential creation/validation where the map may not yet have the entry.
    /// </summary>
    public static (string Domain, string Username) ResolveDomainAndUsernameWithFallback(
        string sid, string fallbackUsername, ISidResolver sidResolver)
    {
        var resolved = sidResolver.TryResolveName(sid);
        if (resolved != null)
            return (ExtractDomain(resolved), ExtractUsername(resolved));

        return (string.Empty, fallbackUsername);
    }

    private static string? GetMapFallback(string sid, IReadOnlyDictionary<string, string>? sidNames)
    {
        if (sidNames != null && sidNames.TryGetValue(sid, out var name) && !string.IsNullOrEmpty(name))
            return name;
        return null;
    }
}