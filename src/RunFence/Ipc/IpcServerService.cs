using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using RunFence.Core;
using RunFence.Core.Ipc;

namespace RunFence.Ipc;

public class IpcServerService(ILoggingService log) : IIpcServerService
{
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public void Start(Func<IpcMessage, string?, string?, bool, IpcResponse> handler)
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

        var options = PipeOptions.Asynchronous;
        if (firstInstance)
            options |= PipeOptions.FirstPipeInstance;

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

    private async Task ListenLoop(string pipeName, Func<IpcMessage, string?, string?, bool, IpcResponse> handler, CancellationToken ct)
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

                var buffer = new byte[Constants.MaxPipeMessageSize];
                int bytesRead = await pipeServer.ReadAsync(buffer, ct);

                // Assemble multi-chunk messages when the pipe signals incomplete delivery.
                // Guard against excessively large messages to prevent memory exhaustion.
                if (!pipeServer.IsMessageComplete)
                {
                    const long maxAssembledSize = 2L * Constants.MaxPipeMessageSize;
                    using var ms = new MemoryStream();
                    ms.Write(buffer, 0, bytesRead);
                    bool overflow = false;
                    while (!pipeServer.IsMessageComplete)
                    {
                        int chunk = await pipeServer.ReadAsync(buffer, ct);
                        if (chunk == 0)
                            break;
                        ms.Write(buffer, 0, chunk);
                        if (ms.Length > maxAssembledSize)
                        {
                            log.Warn("IPC message exceeded maximum size; dropping connection.");
                            overflow = true;
                            break;
                        }
                    }

                    if (!overflow)
                    {
                        var assembled = ms.ToArray();
                        if (assembled.Length <= buffer.Length)
                        {
                            Buffer.BlockCopy(assembled, 0, buffer, 0, assembled.Length);
                        }
                        else
                        {
                            // assembled is valid but larger than the fixed buffer — use it directly
                            buffer = assembled;
                        }

                        bytesRead = assembled.Length;
                    }
                    else
                    {
                        bytesRead = 0;
                    }
                }

                // Capture caller identity, SID, and admin status via impersonation.
                // ImpersonateNamedPipeClient requires the server to have read at least one
                // message first — calling it before ReadAsync can return ERROR_CANNOT_IMPERSONATE.
                string? callerIdentity = null;
                string? callerSid = null;
                bool isAdmin = false;
                try
                {
                    pipeServer.RunAsClient(() =>
                    {
                        using var clientIdentity = WindowsIdentity.GetCurrent();
                        callerIdentity = clientIdentity.Name;
                        callerSid = clientIdentity.User?.Value;
                        var principal = new WindowsPrincipal(clientIdentity);
                        isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
                    });
                }
                catch (Exception ex)
                {
                    log.Error("Failed to identify IPC caller", ex);
                }

                if (bytesRead > 0)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    log.Info($"IPC received: {json.Length} bytes from {callerIdentity ?? "unknown"}");

                    IpcResponse response;
                    try
                    {
                        var message = JsonSerializer.Deserialize<IpcMessage>(json, JsonDefaults.Options);
                        if (message == null)
                        {
                            response = new IpcResponse { Success = false, ErrorMessage = "Invalid message format." };
                        }
                        else
                        {
                            // Suppress ExecutionContext flow before invoking the handler.
                            // ImpersonateNamedPipeClient (called inside RunAsClient above) sets the
                            // Win32 thread token AND may contaminate the managed SecurityContext inside
                            // ExecutionContext. RevertToSelf reverts the Win32 token but does NOT
                            // necessarily revert the managed SecurityContext. Without suppression,
                            // Control.Invoke captures the contaminated SecurityContext via
                            // ExecutionContext.Capture() and ExecutionContext.Run() temporarily
                            // re-applies the impersonated identity on the UI thread, causing
                            // ProtectedData.Unprotect (DPAPI) to fail with NTE_BAD_KEY_STATE.
                            // Suppressing flow causes Capture() to return null, so Control.Invoke
                            // runs the delegate with the UI thread's own clean SecurityContext.
                            var flowControl = ExecutionContext.SuppressFlow();
                            try
                            {
                                response = handler(message, callerIdentity, callerSid, isAdmin);
                            }
                            finally
                            {
                                flowControl.Undo();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Error("IPC handler error", ex);
                        response = new IpcResponse { Success = false, ErrorMessage = "Internal error." };
                    }

                    var responseJson = JsonSerializer.Serialize(response, JsonDefaults.Options);
                    var responseBytes = Encoding.UTF8.GetBytes(responseJson);
                    await pipeServer.WriteAsync(responseBytes, 0, responseBytes.Length, ct);
                    await pipeServer.FlushAsync(ct);
                }

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