using System.Security.AccessControl;
using System.Security.Principal;

namespace RunFence.Acl.Permissions;

public class DeterministicAclAccessEvaluator : IAclAccessEvaluator
{
    public AclAccessEvaluationResult Evaluate(
        FileSystemSecurity security,
        string accountSid,
        IReadOnlyList<string> accountGroupSids,
        FileSystemRights requiredRights)
    {
        try
        {
            var applicableSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(accountSid))
                applicableSids.Add(accountSid);
            foreach (var sid in accountGroupSids)
            {
                if (!string.IsNullOrWhiteSpace(sid))
                    applicableSids.Add(sid);
            }

            var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));
            var remaining = requiredRights;
            FileSystemRights granted = 0;
            FileSystemRights denied = 0;
            string? blockingSid = null;

            foreach (FileSystemAccessRule rule in rules)
            {
                if (rule.IdentityReference is not SecurityIdentifier sidIdentity)
                    continue;
                if ((rule.PropagationFlags & PropagationFlags.InheritOnly) != 0)
                    continue;
                var sid = sidIdentity.Value;
                if (!applicableSids.Contains(sid))
                    continue;

                var relevant = rule.FileSystemRights & requiredRights;
                if (relevant == 0)
                    continue;

                if (rule.AccessControlType == AccessControlType.Deny)
                {
                    var blocked = remaining & relevant;
                    if (blocked != 0)
                    {
                        denied |= blocked;
                        remaining &= ~blocked;
                        blockingSid ??= sid;
                    }
                }
                else
                {
                    var allow = remaining & relevant;
                    if (allow != 0)
                    {
                        granted |= allow;
                        remaining &= ~allow;
                    }
                }

                if (remaining == 0)
                    break;
            }

            var status = denied != 0
                ? (granted != 0 ? AclAccessEvaluationStatus.Partial : AclAccessEvaluationStatus.Denied)
                : remaining == 0
                    ? AclAccessEvaluationStatus.Allowed
                    : granted != 0 ? AclAccessEvaluationStatus.Partial : AclAccessEvaluationStatus.Denied;

            return new AclAccessEvaluationResult(
                RequestedRights: requiredRights,
                GrantedRights: granted,
                DeniedRights: denied,
                BlockingSid: blockingSid,
                Source: AclAccessEvaluationSource.DeterministicSidSet,
                Status: status,
                Error: null);
        }
        catch (Exception ex)
        {
            return new AclAccessEvaluationResult(
                RequestedRights: requiredRights,
                GrantedRights: 0,
                DeniedRights: 0,
                BlockingSid: null,
                Source: AclAccessEvaluationSource.DeterministicSidSet,
                Status: AclAccessEvaluationStatus.Failed,
                Error: ex.Message);
        }
    }
}
