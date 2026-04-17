namespace RunFence.Acl;

/// <summary>
/// Read-only operations for inspecting grant state.
/// Focused interface for consumers that only need to query grants.
/// </summary>
public interface IGrantInspectionService
{
    /// <summary>
    /// Returns the current ACE/ownership state for a <paramref name="sid"/> on a
    /// <paramref name="path"/>.
    /// </summary>
    GrantRightsState ReadGrantState(string path, string sid, IReadOnlyList<string> groupSids);

    /// <summary>
    /// Returns the availability/broken status for a <paramref name="path"/>+<paramref name="sid"/>
    /// combination. Broken = path exists but no direct ACE for this SID in the expected mode.
    /// </summary>
    PathAclStatus CheckGrantStatus(string path, string sid, bool isDeny);

    /// <summary>
    /// Validates a prospective grant against the DB: throws <see cref="InvalidOperationException"/>
    /// if a same-mode duplicate or an opposite-mode entry exists. No side effects.
    /// </summary>
    void ValidateGrant(string sid, string path, bool isDeny);
}
