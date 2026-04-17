using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core;
using RunFence.Core.Ipc;

namespace RunFence.Ipc;

public class IpcServerService(ILoggingService log, IpcConnectionProcessor processor) : IIpcServerService
{
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public void Start(Func<IpcMessage, IpcCallerContext, IpcResponse> handler)
    {
        _cts = new CancellationTokenSource();
        const string pipeName = Constants.PipeName;

        _listenTask = Task.Run(() => ListenLoop(pipeName, handler, _cts.Token));
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

        var pipeSecurity = BuildPipeSecurity();

        try
        {
            return NamedPipeServerStreamAcl.Create(
                pipeName,
                PipeDirection.InOut,
                8,
                PipeTransmissionMode.Message,
                options,
                Constants.MaxPipeMessageSize,
                Constants.MaxPipeMessageSize,
                pipeSecurity);
        }
        catch (UnauthorizedAccessException)
        {
            // Passing lpSecurityAttributes to CreateNamedPipe fails for non-first pipe instances.
            // Retry with pipeSecurity: null (no lpSecurityAttributes) and request WRITE_DAC
            // explicitly via additionalAccessRights so SetAccessControl can apply the ACL.
            var pipe = NamedPipeServerStreamAcl.Create(
                pipeName,
                PipeDirection.InOut,
                8,
                PipeTransmissionMode.Message,
                options,
                Constants.MaxPipeMessageSize,
                Constants.MaxPipeMessageSize,
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

    private static PipeSecurity BuildPipeSecurity()
    {
        var pipeSecurity = new PipeSecurity();

        var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            adminSid,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        // Deny remote (SMB) pipe access — all authorization is application-level via AllowedIpcCallers
        var networkSid = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            networkSid, PipeAccessRights.FullControl, AccessControlType.Deny));

        // Grant all local users read/write — RunAs-launched processes may lack InteractiveSid.
        // Launch is intentionally allowed for any local user when AllowedIpcCallers is empty
        // (the list is populated with known SIDs on first start). Authorization is application-level.
        var worldSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            worldSid, PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return pipeSecurity;
    }

    /// <remarks>
    /// The server accepts up to 8 concurrent named pipe connections. The pipe DACL denies network
    /// (SMB) access and grants read/write to all local users; authorization is application-level
    /// via <see cref="IIpcCallerAuthorizer"/>. Each connection is rate-limited per caller identity
    /// to one request per 100ms to prevent local flooding.
    /// </remarks>
    private async Task ListenLoop(string pipeName, Func<IpcMessage, IpcCallerContext, IpcResponse> handler, CancellationToken ct)
    {
        bool firstInstance = true;
        NamedPipeServerStream? nextPipe = null;

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

                await processor.ProcessConnectionAsync(pipeServer, handler, ct);

                pipeServer.Disconnect();
            }
            catch (OperationCanceledException)
            {
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
                pipeServer?.Dispose();
            }
        }

        nextPipe?.Dispose();
    }
}
