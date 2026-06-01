using System.Security.AccessControl;
using System.Security.Principal;

namespace RunFence.Acl;

public sealed record ProgramDataPrincipalAccess(
    SecurityIdentifier Principal,
    FileSystemRights Rights,
    InheritanceFlags InheritanceFlags,
    PropagationFlags PropagationFlags);
