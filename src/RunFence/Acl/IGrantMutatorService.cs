using System.Security.AccessControl;
using RunFence.Core.Models;

namespace RunFence.Acl;

/// <summary>
/// Operations that write ACEs or trigger grants: add/remove/update grant, ensure access, fix, and remove all.
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
    GrantOperationResult AddGrant(string sid, string path, bool isDeny,
        SavedRightsState? savedRights = null, string? ownerSid = null);

    /// <summary>
    /// Ensures <paramref name="sid"/> has at least the rights described by
    /// <paramref name="savedRights"/> on <paramref name="path"/>, auto-fixing disk state to match
    /// the DB and never reducing existing access.
    /// </summary>
    GrantOperationResult EnsureAccess(string sid, string path, SavedRightsState savedRights,
        Func<string, string, bool>? confirm = null, bool unelevated = false);

    /// <summary>
    /// Convenience overload of <see cref="EnsureAccess(string,string,SavedRightsState,Func{string,string,bool}?,bool)"/>
    /// that accepts raw <see cref="FileSystemRights"/>.
    /// </summary>
    GrantOperationResult EnsureAccess(string sid, string path, FileSystemRights rights,
        Func<string, string, bool>? confirm = null, bool unelevated = false);

    /// <summary>
    /// Removes the allow or deny ACE for <paramref name="sid"/> on <paramref name="path"/> (when
    /// <paramref name="updateFileSystem"/> is true), removes the <see cref="GrantedPathEntry"/> from
    /// the database, and auto-removes traverse entries no longer needed by other grants.
    /// Returns true if a database entry was found and removed.
    /// </summary>
    bool RemoveGrant(string sid, string path, bool isDeny, bool updateFileSystem);

    /// <summary>
    /// Updates an existing grant's <see cref="GrantedPathEntry.SavedRights"/> and re-applies the
    /// NTFS ACE. Updates the owner if <paramref name="ownerSid"/> is provided.
    /// </summary>
    GrantOperationResult UpdateGrant(string sid, string path, bool isDeny,
        SavedRightsState savedRights, string? ownerSid = null);

    /// <summary>
    /// Re-applies the NTFS ACE for an existing grant entry (does not change the DB).
    /// </summary>
    void FixGrant(string sid, string path, bool isDeny);

    /// <summary>
    /// Removes all grant and traverse entries for <paramref name="sid"/>, reverting NTFS ACEs
    /// (when <paramref name="updateFileSystem"/> is true) and cleaning up the database.
    /// </summary>
    void RemoveAll(string sid, bool updateFileSystem);
}
