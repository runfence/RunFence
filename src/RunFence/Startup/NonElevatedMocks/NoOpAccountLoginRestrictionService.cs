#pragma warning disable CS9113 // Parameter 'real' is intentionally unread — RegisterDecorator constructs the real implementation for DI validation
using RunFence.Account;

namespace RunFence.Startup.NonElevatedMocks;

public sealed class NoOpAccountLoginRestrictionService(IAccountLoginRestrictionService real) : IAccountLoginRestrictionService
{
    // real is injected but unused — RegisterDecorator constructs the real implementation
    // to keep DI validation working; all calls are no-ops in non-elevated debug mode

    public bool IsAccountHidden(string username) => false;
    public bool GetAccountHiddenStateOrThrow(string username) => false;
    public void SetAccountHidden(string username, string sid, bool hidden) { }
    public void RestoreAccountHiddenState(string username, bool hidden) { }
    public bool IsLoginBlockedBySid(string sid) => false;
    public SetLoginBlockedResult SetLoginBlockedBySid(string sid, string username, bool blocked)
        => new(null, null);
    public bool? GetNoLogonState(string sid, string? username) => false;
    public void SetUacAdminEnumeration(bool enumerate) { }
    public int GetCurrentUacAdminEnumeration() => -1;
}
