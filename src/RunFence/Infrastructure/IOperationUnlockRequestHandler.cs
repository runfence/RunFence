namespace RunFence.Infrastructure;

/// <summary>
/// Completes an unlock that exists only to continue a pending operation without showing the main window.
/// </summary>
public interface IOperationUnlockRequestHandler
{
    void RequestOperationUnlock();
    Task<bool> HandleOperationUnlockRequestAsync();
}
