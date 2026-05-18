using RunFence.Infrastructure;

namespace RunFence.IntegrationTests;

internal sealed class IntegrationTestInteractiveUserDesktopProvider : IInteractiveUserDesktopProvider
{
    public string? GetDesktopPath() => null;
    public string? GetTaskBarPath() => null;
    public void InvalidateCache() { }
}
