using System.IO.Pipes;
using RunFence.Core;
using RunFence.Core.Ipc;

namespace RunFence.Ipc;

public class IpcServerService(
    ILoggingService log,
    IpcConnectionProcessor processor,
    IpcPipeSecurityFactory pipeSecurityFactory) : IIpcServerService
{
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public void Start(Func<IpcMessage, IpcCallerContext, IpcResponse> handler)
    {
        _cts = new CancellationTokenSource();
        string pipeName = IpcConstants.PipeName;

        NamedPipeServerStream? initialPipe = null;
        try
        {
            initialPipe = CreatePipe(pipeName, firstInstance: true);
        }
#if DEBUG
        catch (UnauthorizedAccessException)
        {
            log.Info("IPC server not started — another Debug instance owns the pipe.");
            _cts.Dispose();
            _cts = null;
            return;
        }
#else
        catch (UnauthorizedAccessException ex)
        {
            _cts.Dispose();
            _cts = null;
            throw new InvalidOperationException("Failed to create initial IPC pipe instance.", ex);
        }
#endif
        catch (Exception ex)
        {
            _cts.Dispose();
            _cts = null;
            throw new InvalidOperationException("Failed to create initial IPC pipe instance.", ex);
        }

        _listenTask = Task.Run(() => ListenLoop(pipeName, handler, _cts.Token, initialPipe));
        log.Info($"IPC server started on pipe: {pipeName}");
    }

    public void Stop()
    {
        _cts?.Cancel();
        log.Info("IPC server stopping.");
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try
        {
            // Bounded wait is intentional cleanup during shutdown; do not block indefinitely here.
            _listenTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _cts?.Dispose();
        _cts = null;
        _listenTask = null;
    }

    private NamedPipeServerStream CreatePipe(string pipeName, bool firstInstance)
    {
        var options = PipeOptions.Asynchronous;
        if (firstInstance)
            options |= PipeOptions.FirstPipeInstance;

        var pipeSecurity = pipeSecurityFactory.Create();

        try
        {
            return NamedPipeServerStreamAcl.Create(
                pipeName,
                PipeDirection.InOut,
                8,
                PipeTransmissionMode.Message,
                options,
                IpcConstants.MaxPipeMessageSize,
                IpcConstants.MaxPipeMessageSize,
                pipeSecurity);
        }
        catch (UnauthorizedAccessException)
        {
            // Passing lpSecurityAttributes to CreateNamedPipe fails for non-first pipe instances
            // under some existing-pipe timing/security conditions. Create the instance first,
            // then immediately apply the intended DACL.
            var pipe = NamedPipeServerStreamAcl.Create(
                pipeName,
                PipeDirection.InOut,
                8,
                PipeTransmissionMode.Message,
                options,
                IpcConstants.MaxPipeMessageSize,
                IpcConstants.MaxPipeMessageSize,
                pipeSecurity: null,
                additionalAccessRights: PipeAccessRights.ChangePermissions);
            try
            {
                pipe.SetAccessControl(pipeSecurity);
            }
            catch
            {
                pipe.Dispose();
                throw;
            }
            return pipe;
        }
    }

    /// <remarks>
    /// The server accepts up to 8 concurrent named pipe connections. Each accepted connection is
    /// dispatched to a background task immediately so the accept loop never blocks on I/O from a
    /// single caller. A 10-second per-connection read deadline prevents slow-loris wedging.
    /// The pipe DACL denies network (SMB) access and grants read/write to all local users;
    /// authorization is application-level via <see cref="IIpcCallerAuthorizer"/>. Each connection
    /// is rate-limited per caller identity to one request per 100ms to prevent local flooding.
    /// </remarks>
    private async Task ListenLoop(
        string pipeName,
        Func<IpcMessage, IpcCallerContext, IpcResponse> handler,
        CancellationToken ct,
        NamedPipeServerStream? initialPipe)
    {
        bool firstInstance = initialPipe == null;
        NamedPipeServerStream? nextPipe = initialPipe;

        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipeServer = null;
            try
            {
                pipeServer = nextPipe ?? CreatePipe(pipeName, firstInstance);
                firstInstance = false;
                nextPipe = null;

                await pipeServer.WaitForConnectionAsync(ct);

                // Pre-create the next pipe instance before handling this connection
                // so there's no gap where no pipe is listening
                try
                {
                    nextPipe = CreatePipe(pipeName, false);
                }
                catch (Exception ex)
                {
                    log.Error("Failed to pre-create next pipe instance", ex);
                }

                // Transfer ownership to background task — do NOT await; loop returns to WaitForConnectionAsync immediately.
                // A hung caller can no longer wedge the accept loop.
                var connectionPipe = pipeServer;
                pipeServer = null;
                _ = Task.Run(() => ProcessConnectionWithTimeoutAsync(connectionPipe, handler, ct));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (UnauthorizedAccessException) when (firstInstance && DebugHelper.IsDebugBuild)
            {
                log.Info("IPC server not started — another Debug instance owns the pipe.");
                break;
            }
            catch (Exception ex)
            {
                log.Error("IPC server error", ex);
                try
                {
                    await Task.Delay(100, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            finally
            {
                pipeServer?.Dispose(); // null when ownership was transferred to background task
            }
        }

        nextPipe?.Dispose();
    }

    private async Task ProcessConnectionWithTimeoutAsync(
        NamedPipeServerStream pipe,
        Func<IpcMessage, IpcCallerContext, IpcResponse> handler,
        CancellationToken serverCt)
    {
        await using (pipe)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(serverCt);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                await processor.ProcessConnectionAsync(pipe, handler, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!serverCt.IsCancellationRequested)
            {
                log.Warn("IPC connection read timed out; closing connection.");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                log.Error("IPC connection processing error", ex);
            }
            finally
            {
                try { pipe.Disconnect(); } catch { }
            }
        }
    }
}
