using RunFence.Core.Models;

namespace RunFence.Acl.UI;

/// <summary>
/// Represents a pending modification to an existing grant entry, capturing both the original state
/// needed to correctly revert or reset ownership during apply and the new desired state to apply
/// without mutating the live DB entry prematurely.
/// </summary>
/// <param name="Entry">The live DB entry (not mutated until Apply).</param>
/// <param name="WasIsDeny">The IsDeny value from NTFS before any mode switch.</param>
/// <param name="WasOwn">The Own value at the time the entry was first tracked as a modification.</param>
/// <param name="NewIsDeny">The desired IsDeny value to apply; equals entry.IsDeny for pure rights-only changes.</param>
/// <param name="NewRights">The desired SavedRights to apply; null means no pending rights change (entry.SavedRights remains in effect).</param>
public record PendingModification(GrantedPathEntry Entry, bool WasIsDeny, bool WasOwn, bool NewIsDeny, SavedRightsState? NewRights);
