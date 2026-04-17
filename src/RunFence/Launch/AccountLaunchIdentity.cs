using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch.Tokens;

namespace RunFence.Launch;

public record AccountLaunchIdentity(string Sid) : LaunchIdentity
{
    public override string Sid { get; } = Sid;
    public PrivilegeLevel? PrivilegeLevel { get; init; }
    public LaunchCredentials? Credentials { get; init; }

    public static AccountLaunchIdentity CurrentAccountBasic =>
        new(SidResolutionHelper.GetCurrentUserSid()) { PrivilegeLevel = Core.Models.PrivilegeLevel.Basic };
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
