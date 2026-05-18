using System.ComponentModel;
using System.IO.Pipes;
using RunFence.Core;

namespace RunFence.Infrastructure;

public sealed class JobKeeperLaunchIpcClient(
    ILoggingService log,
    IJobKeeperRegistry registry,
    IJobKeeperProcessTerminator processTerminator) : IJobKeeperLaunchIpcClient
{
    public async Task<int> SendLaunchRequestAsync(
        string sid,
        bool isLow,
        JobKeeperLaunchRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!registry.TryGet(sid, isLow, out var state))
            return 0;

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
                return 0;
            }

            if (response.Error != 0)
            {
                var win32Message = new Win32Exception(response.Error).Message;
                var message = $"JobKeeper: keeper failed to launch '{request.ExePath}' for {sid}: Win32 error (0x{response.Error:X8}): {win32Message}";
                log.Warn(message);
                throw new JobKeeperChildLaunchException(message, response.Error);
            }

            return response.Pid;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            log.Warn($"JobKeeper: launch IPC timed out for {sid}");
            RemoveAndKill(sid, isLow, state, pid);
            return 0;
        }
        catch (Exception ex) when (ex is not JobKeeperChildLaunchException)
        {
            log.Error($"JobKeeper: pipe error sending request for {sid}: {ex.Message}");
            RemoveAndKill(sid, isLow, state, pid);
            return 0;
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
