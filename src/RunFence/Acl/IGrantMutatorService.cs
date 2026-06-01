using System.Security.AccessControl;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Acl;

/// <summary>
/// Operations that write ACEs or trigger grants: add/remove/update grant, ensure access, and fix.
/// Focused interface for consumers that only need to mutate grants.
/// </summary>
public interface IGrantMutatorService
{
    /// <summary>
    /// Applies an allow or deny ACE for <paramref name="sid"/> on <paramref name="path"/>,
    /// records a <see cref="GrantedPathEntry"/> in the database, and — for allow grants —
    /// auto-adds traverse on the grant's directory and syncs to the interactive user for
    /// container SIDs.
    /// <para>
    /// Duplicate same-mode entry: updates <see cref="GrantedPathEntry.SavedRights"/> in-place
    /// (no new entry added). Opposite-mode entry already exists: throws
    /// <see cref="InvalidOperationException"/>.
    /// </para>
    /// <para>
    /// Null <paramref name="savedRights"/>: applies <see cref="SavedRightsState.DefaultForMode"/>.
    /// </para>
    /// </summary>
    GrantApplyResult AddGrant(string accountSid, string path, bool isDeny,
        SavedRightsState? savedRights, Func<bool>? confirm, IGrantIntentStore? store = null);

    /// <summary>
    /// Ensures <paramref name="sid"/> has at least the rights described by
    /// <paramref name="savedRights"/> on <paramref name="path"/>, auto-fixing disk state to match
    /// the DB and never reducing existing access.
    /// </summary>
    GrantApplyResult EnsureAccess(string sid, string path, SavedRightsState savedRights,
        Func<string, string, bool>? confirm = null, bool unelevated = false);

    /// <summary>
    /// Convenience overload of <see cref="EnsureAccess(string,string,SavedRightsState,Func{string,string,bool}?,bool)"/>
    /// that accepts raw <see cref="FileSystemRights"/>.
    /// </summary>
    GrantApplyResult EnsureAccess(string sid, string path, FileSystemRights rights,
        Func<string, string, bool>? confirm = null, bool unelevated = false);

    /// <summary>
    /// Ensures transient effective access without persisting a new or widened target grant entry.
    /// This may repair disk state for an already-tracked grant, but widening tracked rights must go
    /// through <see cref="EnsureAccess(string,string,SavedRightsState,Func{string,string,bool}?,bool)"/>.
    /// </summary>
    GrantApplyResult EnsureTemporaryAccess(string sid, string path, SavedRightsState savedRights,
        Func<string, string, bool>? confirm = null, bool unelevated = false);

    GrantApplyResult EnsureTemporaryAccess(string sid, string path, FileSystemRights rights,
        Func<string, string, bool>? confirm = null, bool unelevated = false);

    /// <summary>
    /// Removes the allow or deny ACE for <paramref name="sid"/> on <paramref name="path"/>,
    /// removes the <see cref="GrantedPathEntry"/> from the database, and auto-removes traverse
    /// entries no longer needed by other grants.
    /// </summary>
    GrantApplyResult RemoveGrant(string accountSid, string path, bool isDeny);

    /// <summary>
    /// Restores the tracked grant at <paramref name="path"/> to the exact prior
    /// <paramref name="previousEntry"/> state and re-applies the corresponding NTFS ACE.
    /// When <paramref name="previousEntry"/> is null, removes the current tracked grant.
    /// </summary>
    GrantApplyResult RestoreGrant(string accountSid, string path, bool isDeny,
        GrantIntentRestoreSnapshot previousState);

    /// <summary>
    /// Updates an existing grant's <see cref="GrantedPathEntry.SavedRights"/> and re-applies the
    /// NTFS ACE.
    /// </summary>
    GrantApplyResult UpdateGrant(string accountSid, string path, bool isDeny,
        SavedRightsState savedRights, Func<bool>? confirm, IGrantIntentStore? store = null);

    GrantApplyResult SwitchGrantMode(string accountSid, string path, bool newIsDeny,
        SavedRightsState savedRights, Func<bool>? confirm, IGrantIntentStore? store = null);

    GrantApplyResult UntrackGrant(string accountSid, string path, bool isDeny);

    /// <summary>
    /// Re-applies the NTFS ACE for an existing grant entry (does not change the DB).
    /// </summary>
    void FixGrant(string sid, string path, bool isDeny);

    GrantApplyResult FixGrantAcl(string accountSid, string path, bool isDeny);
}
