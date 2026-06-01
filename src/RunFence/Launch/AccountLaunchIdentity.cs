using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Launch;

public record AccountLaunchIdentity(string Sid) : LaunchIdentity
{
    public override string Sid { get; } = Sid;
    public PrivilegeLevel? PrivilegeLevel { get; init; }
    /// <summary>
    /// When non-null, caller retains ownership and must dispose after the launch call returns.
    /// When null, <c>ProcessLauncher</c> looks up credentials internally and disposes them.
    /// </summary>
    public LaunchCredentials? Credentials { get; init; }
    public AssociationResolutionPolicy AssociationResolutionPolicy { get; init; } = AssociationResolutionPolicy.RequireSameAccount;

    public static AccountLaunchIdentity CurrentAccountBasic =>
        new(SidResolutionHelper.GetCurrentUserSid()) { PrivilegeLevel = Core.Models.PrivilegeLevel.Basic };
    public static AccountLaunchIdentity CurrentAccountIsolated =>
        new(SidResolutionHelper.GetCurrentUserSid()) { PrivilegeLevel = Core.Models.PrivilegeLevel.Isolated };
    public static AccountLaunchIdentity CurrentAccountElevated =>
        new(SidResolutionHelper.GetCurrentUserSid()) { PrivilegeLevel = Core.Models.PrivilegeLevel.HighestAllowed };

    public static AccountLaunchIdentity InteractiveUser =>
        new(SidResolutionHelper.GetInteractiveUserSid() ?? throw new InvalidOperationException("Interactive user is unavailable (explorer not running)."));

    public override bool? IsUnelevated => PrivilegeLevel switch
    {
        null => null,
        Core.Models.PrivilegeLevel.HighestAllowed => false,
        _ => true
    };

    public override TResult Visit<TResult>(ILaunchIdentityAcceptor<TResult> acceptor, ProcessLaunchTarget? target)
        => acceptor.Accept(this, target);
}
