using RunFence.Core.Models;

namespace RunFence.Core;

/// <summary>
/// Instance-based resolver for displaying SID-related names in the UI.
/// Holds an <see cref="ISidResolver"/> to avoid allocating a new one on every call,
/// replacing the obsolete static overloads on <see cref="SidResolutionHelper"/> that
/// created a <c>new SidResolver()</c> per invocation.
///
/// <para>Inject via constructor DI where name-resolution is needed without already holding
/// an <see cref="ISidResolver"/>. Classes that already hold an <see cref="ISidResolver"/>
/// should call the corresponding static overloads on <see cref="SidNameResolver"/> directly.</para>
/// </summary>
public class SidDisplayNameResolver(ISidResolver sidResolver)
{
    /// <summary>
    /// Resolves a display name for an arbitrary SID using the standard fallback chain:
    /// 1. Pre-resolved live name  2. Registry profile path  3. Central SidNames map  4. Raw SID
    /// </summary>
    public string GetDisplayName(string sid, string? preResolvedName,
        IReadOnlyDictionary<string, string>? sidNames)
        => SidNameResolver.GetDisplayName(sid, preResolvedName, sidResolver, sidNames);

    /// <summary>
    /// Resolves a display name for a credential entry (includes "(current)" / "(interactive)" suffixes).
    /// </summary>
    public string GetDisplayName(CredentialEntry cred, IReadOnlyDictionary<string, string>? sidNames)
        => SidNameResolver.GetDisplayName(cred, sidResolver, sidNames);

    /// <summary>
    /// Resolves a SID to a bare username string using the standard fallback chain.
    /// Returns null if no source provides a name.
    /// </summary>
    public string? ResolveUsername(string sid, IReadOnlyDictionary<string, string>? sidNames)
        => SidNameResolver.ResolveUsername(sid, sidResolver, sidNames);
}