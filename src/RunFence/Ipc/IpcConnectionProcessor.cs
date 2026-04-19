using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using RunFence.Core;
using RunFence.Core.Ipc;

namespace RunFence.Ipc;

/// <summary>
/// Processes a single accepted named-pipe connection: assembles the message, short-circuits for raw
/// pings, extracts caller identity, applies per-caller rate limiting, deserializes, suppresses
/// ExecutionContext flow, dispatches to the handler, and writes the response.
/// </summary>
public class IpcConnectionProcessor(ILoggingService log, IIpcIdentityExtractor identityExtractor)
{
    private readonly Dictionary<string, DateTime> _lastRequestByCaller = new(StringComparer.OrdinalIgnoreCase);
    private static readonly byte[] RateLimitedBytes = [IpcCommands.RateLimitedSignal];

    public async Task ProcessConnectionAsync(
        NamedPipeServerStream pipe,
        Func<IpcMessage, IpcCallerContext, IpcResponse> dispatch,
        CancellationToken ct)
    {
        var (messageBuffer, bytesRead) = await AssembleMessage(pipe, ct);

        if (bytesRead == 0)
            return;

        // Ping bypasses identity extraction and rate limiting — PONG is cheaper than any rejection response.
        if (bytesRead == IpcCommands.PingBytes.Length
            && messageBuffer.AsSpan(0, bytesRead).SequenceEqual(IpcCommands.PingBytes))
        {
            await pipe.WriteAsync(IpcCommands.PongBytes, 0, IpcCommands.PongBytes.Length, ct);
            await pipe.FlushAsync(ct);
            return;
        }

        var context = identityExtractor.Extract(pipe);

        // Per-caller rate limit: reject requests arriving faster than 100ms from the same identity.
        // Keyed by verified SID (from impersonation) or caller identity/unknown as fallback.
        var now = DateTime.UtcNow;
        var rateLimitKey = context.RateLimitKey;

        // Evict stale entries (older than 10 seconds) to prevent unbounded dictionary growth
        foreach (var staleKey in _lastRequestByCaller
            .Where(kvp => (now - kvp.Value).TotalSeconds > 10)
            .Select(kvp => kvp.Key)
            .ToList())
            _lastRequestByCaller.Remove(staleKey);

        if (_lastRequestByCaller.TryGetValue(rateLimitKey, out var last)
            && (now - last).TotalMilliseconds < 100)
        {
            log.Warn($"IPC rate limit exceeded for caller '{rateLimitKey}'; dropping connection.");
            try
            {
                await pipe.WriteAsync(RateLimitedBytes, 0, RateLimitedBytes.Length, ct);
                await pipe.FlushAsync(ct);
            }
            catch (Exception)
            {
                // Best-effort: ignore errors writing the rate-limit response
            }
            return;
        }

        _lastRequestByCaller[rateLimitKey] = now;

        var json = Encoding.UTF8.GetString(messageBuffer, 0, bytesRead);
        log.Info($"IPC received: {json.Length} bytes from {context.CallerIdentity ?? "unknown"}");

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
                // Use CallerName from the message as fallback when impersonation failed
                // and callerIdentity could not be resolved via the pipe token.
                var effectiveContext = context.CallerIdentity == null && message.CallerName != null
                    ? context with { CallerIdentity = message.CallerName }
                    : context;

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
                    response = dispatch(message, effectiveContext);
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
        await pipe.WriteAsync(responseBytes, 0, responseBytes.Length, ct);
        await pipe.FlushAsync(ct);
    }

    /// <summary>
    /// Reads and assembles a complete IPC message from the pipe, handling multi-chunk delivery.
    /// Returns the message buffer and the number of valid bytes. Returns (buffer, 0) if the message
    /// exceeded the maximum allowed size.
    /// </summary>
    private async Task<(byte[] buffer, int bytesRead)> AssembleMessage(NamedPipeServerStream pipe, CancellationToken ct)
    {
        var buffer = new byte[Constants.MaxPipeMessageSize];
        int bytesRead = await pipe.ReadAsync(buffer, ct);

        if (pipe.IsMessageComplete)
            return (buffer, bytesRead);

        const long maxAssembledSize = 2L * Constants.MaxPipeMessageSize;
        using var ms = new MemoryStream();
        ms.Write(buffer, 0, bytesRead);
        bool overflow = false;
        while (!pipe.IsMessageComplete)
        {
            int chunk = await pipe.ReadAsync(buffer, ct);
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

        if (overflow)
            return (buffer, 0);

        var assembled = ms.ToArray();
        if (assembled.Length <= buffer.Length)
        {
            Buffer.BlockCopy(assembled, 0, buffer, 0, assembled.Length);
            return (buffer, assembled.Length);
        }

        return (assembled, assembled.Length);
    }
}
