using RunFence.Core.Models;

namespace RunFence.Acl.UI;

/// <summary>
/// Performs low-level NTFS operations for individual grant entries:
/// initial grant application, ACE revert, and ownership changes.
/// Called from <see cref="AclManagerApplyOrchestrator"/> on a background thread.
/// </summary>
public class GrantEntryNtfsOperations
{
    private readonly IGrantedPathAclService _aclService;
    private string _sid = null!;
    private IReadOnlyList<string> _groupSids = null!;

    public GrantEntryNtfsOperations(IGrantedPathAclService aclService)
    {
        _aclService = aclService;
    }

    public void Initialize(string sid, IReadOnlyList<string> groupSids)
    {
        _sid = sid;
        _groupSids = groupSids;
    }

    /// <summary>Applies the baseline ACE for a newly added grant entry.</summary>
    public void ApplyInitialGrant(GrantedPathEntry entry)
    {
        if (entry.IsDeny)
            _aclService.ApplyDenyRights(entry.Path, _sid, new DenyRights(false, false));
        else
            _aclService.ApplyReadOnlyGrant(entry.Path, _sid);
    }

    /// <summary>Reverts the ACE for a single grant entry from the filesystem.</summary>
    public void RevertGrantEntry(GrantedPathEntry entry)
    {
        _aclService.RevertGrant(entry.Path, _sid, entry.IsDeny);
    }

    /// <summary>
    /// Applies ownership changes for a path. Called from Apply processing on a background thread.
    /// Allow+checked → ChangeOwner to SID.
    /// Allow+unchecked → ResetOwner.
    /// Deny+checked → ResetOwner only if currently owned by this SID; otherwise no-op.
    /// Deny+unchecked → no-op.
    /// </summary>
    public void ApplyOwnerChange(GrantedPathEntry entry, CheckState checkState, bool isDirectory)
    {
        if (!entry.IsDeny)
        {
            if (checkState == CheckState.Checked)
                _aclService.ChangeOwner(entry.Path, _sid, isDirectory);
            else
                _aclService.ResetOwner(entry.Path, isDirectory);
        }
        else
        {
            if (checkState == CheckState.Checked)
            {
                // Only reset to admin owner if this SID currently owns the path.
                var currentState = _aclService.ReadRights(entry.Path, _sid, _groupSids);
                if (currentState.IsAccountOwner == CheckState.Checked)
                    _aclService.ResetOwner(entry.Path, isDirectory);
                // else: already not owned by this SID — no-op.
            }
            // Deny+unchecked → no-op (don't enforce ownership).
        }
    }
}