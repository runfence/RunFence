using RunFence.Acl.UI;

namespace RunFence.RunAs;

public sealed record RunAsCreatedAccountPostSetupResult(
    AncestorPermissionResult? PermissionGrant,
    bool WasCanceled,
    IReadOnlyList<string> WarningMessages);
