#pragma warning disable CS9113 // Parameter 'real' is intentionally unread — RegisterDecorator constructs the real implementation for DI validation
using RunFence.Account;

namespace RunFence.Startup.NonElevatedMocks;

public sealed class MockProfileRepairHelper(IProfileRepairHelper real) : IProfileRepairHelper
{
    // real is injected but unused — RegisterDecorator constructs the real implementation
    // to keep DI validation working; profile repair is skipped (no HKLM access needed) in non-elevated debug mode

    public T ExecuteWithProfileRepair<T>(Func<T> launchAction, string? accountSid)
        => launchAction();
}
