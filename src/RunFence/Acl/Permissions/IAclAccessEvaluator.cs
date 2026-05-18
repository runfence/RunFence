using System.Security.AccessControl;

namespace RunFence.Acl.Permissions;

public interface IAclAccessEvaluator
{
    AclAccessEvaluationResult Evaluate(
        FileSystemSecurity security,
        string accountSid,
        IReadOnlyList<string> accountGroupSids,
        FileSystemRights requiredRights);
}

