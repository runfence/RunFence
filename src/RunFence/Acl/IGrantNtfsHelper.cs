using System.Security.AccessControl;
using RunFence.Core.Models;

namespace RunFence.Acl;

/// <summary>
/// Low-level NTFS ACE operations (apply, revert, read, check, ownership).
/// All operations are direct NTFS reads/writes with no mutable state.
/// </summary>
public interface IGrantNtfsHelper
{
    void ApplyAce(string path, string sid, bool isDeny, SavedRightsState rights, bool isFolder);
    void RevertAce(string path, string sid, bool isDeny);
    FileSystemSecurity GetSecurity(string path);
    GrantRightsState ReadGrantState(string path, string sid, IReadOnlyList<string> groupSids);
    PathAclStatus CheckGrantStatus(string path, string sid, bool isDeny);
    void ChangeOwner(string path, string sid, bool recursive);
    void ResetOwner(string path, bool recursive);
    bool PathExists(string path, out bool isFolder);
}
