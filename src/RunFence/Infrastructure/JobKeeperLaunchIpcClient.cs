using System.ComponentModel;
using System.IO.Pipes;
using RunFence.Core;

namespace RunFence.Infrastructure;

public sealed class JobKeeperLaunchIpcClient(
    ILoggingService log,
    IJobKeeperRegistry registry,
    IJobKeeperProcessTerminator processTerminator,
    IJobObjectApi jobObjectApi,
    IJobKeeperProcessHandleOpener processHandleOpener) : IJobKeeperLaunchIpcClient
{
    private const uint SynchronizeAccess = 0x00100000;

    public async Task<JobKeeperLaunchedProcess?> SendLaunchRequestAsync(
        string sid,
        bool isLow,
        JobKeeperLaunchRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!registry.TryGet(sid, isLow, out var state))
            return null;

        NamedPipeServerStream pipe;
        int pid;
        SemaphoreSlim ipcGate;
        lock (state.SyncRoot)
        {
            pipe = state.Pipe;
            pid = state.Pid;
            ipcGate = state.IpcGate;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var gateHeld = false;
        try
        {
            await ipcGate.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            gateHeld = true;

            await JobKeeperProtocol.WriteMessageAsync(pipe, request, timeoutCts.Token).ConfigureAwait(false);
            var response = await JobKeeperProtocol.ReadMessageAsync<JobKeeperLaunchResponse>(pipe, timeoutCts.Token).ConfigureAwait(false);
            if (response == null)
            {
                log.Warn($"JobKeeper: null response from keeper for {sid}");
                RemoveAndKill(sid, isLow, state, pid);
                return null;
            }

            if (response.Error != 0)
            {
                var win32Message = new Win32Exception(response.Error).Message;
                var message = $"JobKeeper: keeper failed to launch '{request.ExePath}' for {sid}: Win32 error (0x{response.Error:X8}): {win32Message}";
                log.Warn(message);
                throw new JobKeeperChildLaunchException(message, response.Error);
            }

            var duplicatedHandleValue = 0L;
            if (response.ProcessHandleValue != 0)
            {
                var sourceProcessHandle = state.ProcessHandle;
                var closeSourceProcessHandle = false;
                if (sourceProcessHandle == IntPtr.Zero)
                {
                    sourceProcessHandle = processHandleOpener.OpenForDuplicate(pid);
                    if (sourceProcessHandle == IntPtr.Zero)
                    {
                        log.Warn($"JobKeeper: failed to open keeper process handle for {sid} (pid={pid})");
                        RemoveAndKill(sid, isLow, state, pid);
                        return null;
                    }

                    closeSourceProcessHandle = true;
                }

                try
                {
                    if (!jobObjectApi.DuplicateHandleToProcess(
                            sourceProcessHandle,
                            new IntPtr(response.ProcessHandleValue),
                            ProcessNative.GetCurrentProcess(),
                            SynchronizeAccess | ProcessNative.ProcessQueryLimitedInformation,
                            out var duplicatedHandle))
                    {
                        log.Warn($"JobKeeper: failed to duplicate launched child handle for {sid} (pid={response.Pid})");
                        RemoveAndKill(sid, isLow, state, pid);
                        return null;
                    }

                    duplicatedHandleValue = duplicatedHandle.ToInt64();
                }
                finally
                {
                    if (closeSourceProcessHandle)
                        processHandleOpener.Close(sourceProcessHandle);
                }
            }

            return new JobKeeperLaunchedProcess(response.Pid, duplicatedHandleValue);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            log.Warn($"JobKeeper: launch IPC timed out for {sid}");
            RemoveAndKill(sid, isLow, state, pid);
            return null;
        }
        catch (Exception ex) when (ex is not JobKeeperChildLaunchException)
        {
            log.Error($"JobKeeper: pipe error sending request for {sid}: {ex.Message}");
            RemoveAndKill(sid, isLow, state, pid);
            return null;
        }
        finally
        {
            if (gateHeld)
            {
                try
                {
                    ipcGate.Release();
                }
                catch (ObjectDisposedException)
                {
                }
                catch (SemaphoreFullException)
                {
                }
            }
        }
    }

    private void RemoveAndKill(string sid, bool isLow, JobKeeperState state, int pid)
    {
        registry.RemoveAndDispose(sid, isLow, state);
        processTerminator.Kill(pid);
    }

}
