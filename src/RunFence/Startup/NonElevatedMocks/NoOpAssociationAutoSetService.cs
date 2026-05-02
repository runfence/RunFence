#pragma warning disable CS9113 // Parameter 'real' is intentionally unread — RegisterDecorator constructs the real implementation for DI validation
using RunFence.Apps;

namespace RunFence.Startup.NonElevatedMocks;

public sealed class NoOpAssociationAutoSetService(IAssociationAutoSetService real) : IAssociationAutoSetService
{
    // real is injected but unused — RegisterDecorator constructs the real implementation
    // to keep DI validation working; all HKCU association writes are suppressed in non-elevated debug mode
    // to prevent conflicts with the real release RunFence's HKCU associations

    public void AutoSetForAllUsers() { }
    public void AutoSetForUser(string sid) { }
    public void ForceAutoSetForUser(string sid) { }
    public void RestoreForAllUsers() { }
    public void RestoreForUser(string sid) { }
    public void RestoreKeyForAllUsers(string key) { }
}
