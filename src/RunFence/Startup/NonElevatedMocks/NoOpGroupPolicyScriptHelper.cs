#pragma warning disable CS9113 // Parameter 'real' is intentionally unread — RegisterDecorator constructs the real implementation for DI validation
using RunFence.Account;

namespace RunFence.Startup.NonElevatedMocks;

public sealed class NoOpGroupPolicyScriptHelper(IGroupPolicyScriptHelper real) : IGroupPolicyScriptHelper
{
    // real is injected but unused — RegisterDecorator constructs the real implementation
    // to keep DI validation working; all calls are no-ops in non-elevated debug mode

    public bool IsLoginBlocked(string sid) => false;
    public SetLoginBlockedResult SetLoginBlocked(string sid, bool blocked) => new(null, null);
}
