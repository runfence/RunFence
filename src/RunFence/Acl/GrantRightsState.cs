namespace RunFence.Acl;

/// <summary>Current ACE/ownership state for a path+SID combination.</summary>
public record GrantRightsState(
    RightCheckState AllowExecute,
    RightCheckState AllowWrite,
    RightCheckState AllowSpecial,
    RightCheckState DenyRead,
    RightCheckState DenyExecute,
    RightCheckState DenyWrite,
    RightCheckState DenySpecial,
    RightCheckState TraverseOnlyAllow,
    RightCheckState TraverseOnlyDeny,
    RightCheckState IsAccountOwner,
    bool IsAdminOwner,
    int DirectAllowAceCount,
    int DirectDenyAceCount);
