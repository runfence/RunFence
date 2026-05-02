#pragma warning disable CS9113 // Parameter 'real' is intentionally unread — RegisterDecorator constructs the real implementation for DI validation
using RunFence.Apps;
using RunFence.Core.Models;

namespace RunFence.Startup.NonElevatedMocks;

public sealed class NoOpAppHandlerRegistrationService(IAppHandlerRegistrationService real) : IAppHandlerRegistrationService
{
    // real is injected but unused — RegisterDecorator constructs the real implementation
    // to keep DI validation working; all calls are no-ops in non-elevated debug mode

    public void Sync(Dictionary<string, HandlerMappingEntry> effectiveHandlerMappings, List<AppEntry> apps) { }
    public void UnregisterAll() { }
}
