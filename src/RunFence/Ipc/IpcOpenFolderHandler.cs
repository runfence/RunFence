using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.Infrastructure;

namespace RunFence.Ipc;

/// <summary>
/// Handles the IPC OpenFolder command: validates the path and opens the folder in Explorer.
/// </summary>
public class IpcOpenFolderHandler(
    IAppStateProvider appState,
    IAppLockControl appLock,
    IUiThreadInvoker uiThreadInvoker,
    IDirectoryValidator? directoryValidator,
    ILoggingService log,
    IShellFolderOpener shellFolderOpener)
{
    public IpcResponse HandleOpenFolder(IpcMessage message, string? callerIdentity, string? callerSid)
    {
        if (string.IsNullOrEmpty(message.Arguments))
            return new IpcResponse { Success = false, ErrorMessage = "Folder path is required." };

        var path = message.Arguments;
        log.Info($"IPC OpenFolder: {path} from SID {callerSid}");

        // No global IPC authorization check here by design: opening a folder in Explorer grants no elevated
        // access. Path safety is enforced below via IDirectoryValidator (including TOCTOU protection).
        // Blocking based on IPC caller lists would silently break the "Show in Folder" feature for accounts
        // not explicitly listed as IPC callers.
        if (string.IsNullOrEmpty(callerSid))
            return new IpcResponse { Success = false, ErrorMessage = "Caller identity could not be determined." };

        if (appState.IsShuttingDown)
            return new IpcResponse { Success = false, ErrorMessage = "Application is shutting down." };

        if (appLock.IsUnlockPolling)
            return new IpcResponse { Success = false, ErrorMessage = "Busy." };

        if (directoryValidator == null)
            return new IpcResponse { Success = false, ErrorMessage = "Directory validation not available." };

        // Validate path and hold the directory handle open to prevent TOCTOU swap.
        var validation = directoryValidator.ValidateAndHold(path, callerSid);
        if (!validation.IsValid)
        {
            validation.Dispose();
            log.Warn($"OpenFolder rejected: {validation.Error}");
            return new IpcResponse { Success = false, ErrorMessage = validation.Error };
        }

        // SHParseDisplayName + ShellExecuteEx require STA COM (the UI thread is STA).
        // Invoke synchronously on the UI thread so we can detect failures.
        IpcResponse? shellResult = null;
        try
        {
            uiThreadInvoker.Invoke(() =>
            {
                try
                {
                    if (!shellFolderOpener.TryOpen(validation.CanonicalPath!, out var shellError))
                    {
                        log.Warn($"OpenFolder: {shellError}");
                        shellResult = new IpcResponse { Success = false, ErrorMessage = "Could not open folder." };
                        return;
                    }

                    shellResult = new IpcResponse { Success = true };
                }
                catch (Exception ex)
                {
                    log.Error("OpenFolder: shell launch failed", ex);
                    shellResult = new IpcResponse { Success = false, ErrorMessage = "Could not open folder." };
                }
            });
        }
        catch (ObjectDisposedException)
        {
            validation.Dispose();
            return new IpcResponse { Success = false, ErrorMessage = "Application is shutting down." };
        }
        catch (InvalidOperationException)
        {
            validation.Dispose();
            return new IpcResponse { Success = false, ErrorMessage = "Application is shutting down." };
        }

        if (shellResult?.Success != true)
        {
            validation.Dispose();
            return shellResult ?? new IpcResponse { Success = false, ErrorMessage = "Internal error." };
        }

        // Hold the directory handle for 5 seconds on a background thread while Explorer reads the path,
        // preventing deletion/rename/swap after the ShellExecuteEx call returns. Return the IPC response
        // immediately so the pipe is free for other callers.
        _ = Task.Run(() =>
        {
            using (validation)
            {
                Thread.Sleep(5000);
            }
        });

        return new IpcResponse { Success = true };
    }
}