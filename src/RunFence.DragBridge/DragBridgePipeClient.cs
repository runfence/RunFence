using System.IO.Pipes;
using RunFence.Core.Ipc;

namespace RunFence.DragBridge;

/// <summary>
/// Handles all named pipe communication for <see cref="DragBridgeWindow"/>:
/// receiving the initial file list and resolve responses from the main app,
/// and sending file drop and resolve request messages.
/// Owns and disposes the pipe.
/// </summary>
public class DragBridgePipeClient(
    NamedPipeClientStream? pipe) : IDisposable
{
    public event Action<List<string>, bool>? InitialFilesReceived;
    public event Action<List<string>>? ResolveSucceeded;
    public event Action? ResolveCancelled;
    public event Action<List<string>>? DropSent;
    public event Action? ResolvePendingCleared;

    /// <summary>
    /// Reads the initial file list from the main app, then loops reading resolve responses
    /// until the pipe closes or the window is disposed.
    /// </summary>
    public async Task ReceiveAndRunAsync()
    {
        try
        {
            var initial = await DragBridgeProtocol.ReadAsync(pipe!);
            var initialFiles = initial?.FilePaths ?? [];
            InitialFilesReceived?.Invoke(initialFiles, initial?.FilesResolved ?? false);

            while (true)
            {
                var response = await DragBridgeProtocol.ReadAsync(pipe!);
                if (response == null)
                    break;

                ResolvePendingCleared?.Invoke();
                if (response.FilePaths.Count > 0)
                    ResolveSucceeded?.Invoke(response.FilePaths);
                else
                    ResolveCancelled?.Invoke();
            }
        }
        catch (IOException)
        {
        } // main app closed pipe - normal
        catch (ObjectDisposedException)
        {
        }
        catch
        {
            ResolveCancelled?.Invoke();
        }
    }

    /// <summary>
    /// Sends a file list (drop event) to the main app and signals the window to update its state.
    /// </summary>
    public async Task<DragBridgeSendDropResult> SendDropAsync(List<string> files)
    {
        if (pipe?.IsConnected != true)
            return new DragBridgeSendDropResult(false, "Connection to RunFence was lost.");

        try
        {
            await DragBridgeProtocol.WriteAsync(pipe,
                new DragBridgeData { FilePaths = files }); // MessageType=FileList (default)
            DropSent?.Invoke(files);
            return new DragBridgeSendDropResult(true, null);
        }
        catch (Exception ex)
        {
            var message = string.IsNullOrWhiteSpace(ex.Message)
                ? "Failed to send dropped files to RunFence."
                : $"Failed to send dropped files to RunFence. {ex.Message}";
            return new DragBridgeSendDropResult(false, message);
        }
    }

    /// <summary>
    /// Sends a resolve request to the main app, asking it to grant cross-user access to the files.
    /// On failure, clears the resolve-pending flag so the user can retry.
    /// </summary>
    public async Task SendResolveRequestAsync()
    {
        try
        {
            if (pipe?.IsConnected == true)
                await DragBridgeProtocol.WriteAsync(pipe,
                    new DragBridgeData { MessageType = DragBridgeMessageType.ResolveRequest });
        }
        catch
        {
            ResolvePendingCleared?.Invoke();
        }
    }

    public void Dispose() => pipe?.Dispose();
}
