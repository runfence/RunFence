using System.IO.Pipes;
using RunFence.Core;
using RunFence.Core.Ipc;

namespace RunFence.DragBridge;

/// <summary>
/// Manages the bridge flow: launching DragBridgeWindow, maintaining captured file state,
/// handling drop events and lazy resolve requests.
/// </summary>
public class DragBridgeCopyFlow(
    IDragBridgePipeLauncher processLauncher,
    INotificationService notifications,
    ILoggingService log,
    ICapturedFileStore capturedFileStore,
    int pipeConnectTimeoutMs = Constants.DragBridgePipeConnectTimeoutMs)
{
    /// <summary>
    /// Returns the captured file paths and source SID, expiring after 5 minutes.
    /// Returns null files if nothing was captured or the capture expired.
    /// </summary>
    public CapturedFilesResult GetCapturedFiles()
        => capturedFileStore.GetCapturedFiles();

    public async Task RunBridgeAsync(
        WindowOwnerInfo ownerInfo,
        List<string> initialFiles,
        Point cursorPos,
        Func<CancellationToken, Task<List<string>?>>? resolveDelegate,
        bool initialFilesResolved,
        nint restoreHwnd,
        CancellationToken ct)
    {
        try
        {
            await using var pipeServer = await LaunchAndConnectAsync(ownerInfo, cursorPos, restoreHwnd, ct);
            if (pipeServer == null)
                return;

            // Write initial files (possibly empty). FilesResolved=true skips the resolve dance in the window.
            await DragBridgeProtocol.WriteAsync(pipeServer,
                new DragBridgeData { FilePaths = initialFiles, FilesResolved = initialFilesResolved }, ct);

            // Read: drops (FileList) or resolve requests (ResolveRequest)
            while (true)
            {
                var data = await DragBridgeProtocol.ReadAsync(pipeServer, ct);
                if (data == null)
                    break;

                if (data.MessageType == DragBridgeMessageType.ResolveRequest && resolveDelegate != null)
                {
                    var resolved = await resolveDelegate(ct);
                    await DragBridgeProtocol.WriteAsync(pipeServer,
                        new DragBridgeData { FilePaths = resolved ?? [] }, ct);
                }
                else if (data.FilePaths.Count > 0) // FileList = new drop
                {
                    capturedFileStore.SetCapturedFiles(data.FilePaths, ownerInfo.Sid.Value);
                    log.Info($"DragBridgeService: captured {data.FilePaths.Count} file(s) via drop.");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        } // normal: window closed mid-read
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            log.Error("DragBridgeService: bridge flow failed", ex);
        }
    }

    private async Task<NamedPipeServerStream?> LaunchAndConnectAsync(
        WindowOwnerInfo ownerInfo, Point cursorPos, nint restoreHwnd, CancellationToken ct)
    {
        var pipeName = $"RunFence-DragBridge-{Guid.NewGuid():N}";
        var pipeServer = processLauncher.CreatePipeServer(pipeName, ownerInfo.Sid);
        bool success = false;
        try
        {
            var args = BuildArgs(pipeName, cursorPos, restoreHwnd);
            var process = processLauncher.LaunchForSid(ownerInfo, args, notifications);
            processLauncher.SetActiveProcess(process);

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(pipeConnectTimeoutMs);

            try
            {
                await pipeServer.WaitForConnectionAsync(connectCts.Token);
            }
            catch (OperationCanceledException)
            {
                log.Error("DragBridgeService: DragBridge did not connect within timeout.");
                processLauncher.KillProcess(process);
                return null;
            }

            if (!processLauncher.VerifyClientProcess(pipeServer, process))
            {
                log.Error("DragBridgeService: pipe client process verification failed — disconnecting.");
                return null;
            }

            // Signal the child to create its window. The child defers window creation
            // until this signal, then uses AttachThreadInput to force foreground.
            processLauncher.SignalReady(pipeServer);

            success = true;
            return pipeServer;
        }
        finally
        {
            if (!success)
                await pipeServer.DisposeAsync();
        }
    }

    internal static List<string> BuildArgs(string pipeName, Point cursorPos, nint restoreHwnd = 0)
        =>
        [
            "--pipe", pipeName, "--x", cursorPos.X.ToString(), "--y", cursorPos.Y.ToString(),
            "--runfence-pid", Environment.ProcessId.ToString(),
            "--restore-hwnd", restoreHwnd.ToString()
        ];
}