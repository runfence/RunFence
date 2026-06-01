using System.Security.AccessControl;
using System.Security.Principal;

namespace RunFence.Acl;

public sealed record ManagedAclRuleSet(
    IReadOnlyList<FileSystemAccessRule> Rules,
    IReadOnlySet<SecurityIdentifier> ManagedSids,
    IReadOnlyList<InvalidAllowAclEntryRule> InvalidEntries);
