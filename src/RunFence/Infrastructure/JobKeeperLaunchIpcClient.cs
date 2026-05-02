using RunFence.Core;

namespace RunFence.Infrastructure;

public sealed class JobKeeperLaunchIpcClient(
    ILoggingService log,
    IJobKeeperRegistry registry,
    IJobKeeperProcessTerminator processTerminator) : IJobKeeperLaunchIpcClient
{
    public int SendLaunchRequest(string sid, bool isLow, JobKeeperLaunchRequest request)
    {
        if (!registry.TryGet(sid, isLow, out var state))
            return 0;

        lock (state.SyncRoot)
        {
            try
            {
                JobKeeperProtocol.WriteMessage(state.Pipe, request);
                var response = JobKeeperProtocol.ReadMessage<JobKeeperLaunchResponse>(state.Pipe);
                if (response == null)
                {
                    log.Warn($"JobKeeper: null response from keeper for {sid}");
                    RemoveAndKill(sid, isLow, state);
                    return 0;
                }

                if (response.Error != 0)
                {
                    log.Warn($"JobKeeper: keeper reported Win32 error {response.Error} launching for {sid}");
                    return 0;
                }

                return response.Pid;
            }
            catch (Exception ex)
            {
                log.Error($"JobKeeper: pipe error sending request for {sid}: {ex.Message}");
                RemoveAndKill(sid, isLow, state);
                return 0;
            }
        }
    }

    private void RemoveAndKill(string sid, bool isLow, JobKeeperState state)
    {
        registry.RemoveAndDispose(sid, isLow, state);
        processTerminator.Kill(state.Pid);
    }
}
