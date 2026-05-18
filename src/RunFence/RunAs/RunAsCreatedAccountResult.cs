using RunFence.Acl.UI;
using RunFence.Core.Models;

namespace RunFence.RunAs;

public sealed record RunAsCreatedAccountResult(
    CredentialEntry Credential,
    AncestorPermissionResult? PermissionGrant);
