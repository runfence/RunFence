using System.Security.AccessControl;

namespace RunFence.Acl;

public interface IExplicitAceAccessor
{
    void ApplyExplicitAce(string path, string sid, AccessControlType type, FileSystemRights rights,
        Func<FileSystemAccessRule, bool>? shouldSkip = null);
    void RemoveExplicitAces(string path, string sid, AccessControlType type,
        Func<FileSystemAccessRule, bool>? shouldSkip = null);
}
