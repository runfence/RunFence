namespace RunFence.Infrastructure;

/// <summary>
/// Handles an unlock request that is already running inside the elevated RunFence process.
/// This is the direct elevated path used only when the IPC caller is the same admin account as
/// the current elevated process.
/// </summary>
public interface IElevatedUnlockRequestHandler
{
    Task<bool> HandleElevatedUnlockRequestAsync();
}
