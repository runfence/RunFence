namespace RunFence.Core.Helpers;

public enum FallbackCleanupMode
{
    RestoreFallbackOnly,
    RemoveRunFenceOverrideThenRestoreFallback,
    NoRegistryCleanup
}
