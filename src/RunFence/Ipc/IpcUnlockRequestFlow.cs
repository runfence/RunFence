using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Core.Ipc;
using RunFence.Infrastructure;

namespace RunFence.Ipc;

public sealed class IpcUnlockRequestFlow(
    IIpcUiInvoker ipcUiInvoker,
    IElevatedUnlockRequestHandler elevatedUnlockRequestHandler,
    IOperationUnlockRequestHandler operationUnlockRequestHandler,
    IShowWindowRequestHandler showWindowRequestHandler,
    ILoggingService log)
{
    public IpcResponse HandleUnlockApp(
        IpcOperationRequest request,
        CancellationToken cancellationToken)
        => HandleUnlockRequest(
            request,
            "Unlock",
            cancellationToken,
            directUnlock: () => elevatedUnlockRequestHandler.HandleElevatedUnlockRequestAsync(),
            indirectUnlock: showWindowRequestHandler.RequestShowWindow,
            cancelledMessage: "Unlock cancelled.",
            failedMessage: "Unlock failed.");

    public IpcResponse HandleUnlockOperation(
        IpcOperationRequest request,
        CancellationToken cancellationToken)
        => HandleUnlockRequest(
            request,
            "Operation unlock",
            cancellationToken,
            directUnlock: () => operationUnlockRequestHandler.HandleOperationUnlockRequestAsync(),
            indirectUnlock: operationUnlockRequestHandler.RequestOperationUnlock,
            cancelledMessage: "No pending operation unlock.",
            failedMessage: "Operation unlock failed.");

    private IpcResponse HandleUnlockRequest(
        IpcOperationRequest request,
        string requestLabel,
        CancellationToken cancellationToken,
        Func<Task<bool>> directUnlock,
        Action indirectUnlock,
        string cancelledMessage,
        string failedMessage)
    {
        if (!request.IsAdmin)
        {
            log.Warn($"{requestLabel} rejected: caller '{request.CallerIdentity}' is not admin");
            return new IpcResponse { Success = false, ErrorMessage = "Access denied. Admin required." };
        }

        var currentUserSid = SidResolutionHelper.GetCurrentUserSid();
        var allowDirectUnlock = SidComparer.SidEquals(request.CallerSid, currentUserSid);
        log.Info(allowDirectUnlock
            ? $"{requestLabel} requested by {request.CallerIdentity}"
            : $"{requestLabel} requested by {request.CallerIdentity}; using normal flow because caller SID '{request.CallerSid}' does not match current SID '{currentUserSid}'");

        if (ipcUiInvoker.IsShuttingDown(out var shuttingDownResponse))
            return shuttingDownResponse!;

        try
        {
            Task<bool>? unlockTask = null;
            if (!ipcUiInvoker.TryInvoke(
                    () =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (allowDirectUnlock)
                            unlockTask = directUnlock();
                        else
                            indirectUnlock();
                    },
                    out var invokeResponse))
            {
                return invokeResponse!;
            }

            if (!allowDirectUnlock)
                return new IpcResponse { Success = true };

            var unlocked = unlockTask!.GetAwaiter().GetResult();
            return unlocked
                ? new IpcResponse { Success = true }
                : new IpcResponse { Success = false, ErrorMessage = cancelledMessage };
        }
        catch (Exception ex)
        {
            log.Error($"{requestLabel} failed", ex);
            return new IpcResponse { Success = false, ErrorMessage = failedMessage };
        }
    }
}
