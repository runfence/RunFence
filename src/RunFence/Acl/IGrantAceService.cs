using System.Security.AccessControl;
using RunFence.Core.Models;

namespace RunFence.Acl;

/// <summary>
/// Low-level NTFS ACE operations for grant entries.
/// </summary>
public interface IGrantAceService : IGrantInspectionService
{
    FileSystemSecurity GetSecurity(string path);
    void ApplyAce(string path, string sid, bool isDeny, SavedRightsState rights, bool isFolder);
    void RevertAce(string path, string sid, bool isDeny);
}
