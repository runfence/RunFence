using System.IO.Pipes;
using System.Security.Principal;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Infrastructure;

public sealed class JobKeeperService(
    ILoggingService log,
    IJobKeeperIdentityStore identityStore,
    IJobKeeperPipeServerFactory pipeServerFactory,
    IJobKeeperProcessDiscovery processDiscovery,
    IJobKeeperProcessVerifier processVerifier,
    IJobKeeperRegistry registry,
    IJobKeeperProcessTerminator processTerminator,
    TimeSpan waitForConnectionTimeout) : IJobKeeperService
{
    public bool HasJobKeeper(string sid, bool isLow) => registry.Has(sid, isLow);

    public int WaitAndRegisterJobKeeper(
        JobKeeperInstanceIdentity identity,
        NamedPipeServerStream pipeServer,
        int expectedPid,
        SecurityIdentifier targetUserSid)
    {
        try
        {
            if (!WaitForConnection(pipeServer, waitForConnectionTimeout))
            {
                log.Warn($"JobKeeper: timed out waiting for connection from job keeper for {identity.TargetSid} ({identity.ExpectedMode})");
                pipeServer.Dispose();
                KillExpectedKeeper(expectedPid);
                return 0;
            }

            var verification = processVerifier.Verify(pipeServer, expectedPid, targetUserSid, identity);
            if (!verification.Succeeded)
            {
                log.Warn($"JobKeeper: verification failed for {identity.TargetSid} ({identity.ExpectedMode}): {verification.FailureReason ?? "unknown reason"}");
                pipeServer.Dispose();
                KillExpectedKeeper(expectedPid);
                return 0;
            }

            var verifiedPid = verification.ProcessId;
            identityStore.UpdateLastVerifiedPid(identity, verifiedPid);
            log.Info($"JobKeeper: registered keeper PID={verifiedPid} for {identity.TargetSid} ({identity.ExpectedMode})");
            registry.Register(
                identity.TargetSid,
                identity.ExpectedMode == JobKeeperIntegrityMode.LowIntegrity,
                new JobKeeperState(pipeServer, verifiedPid));
            return verifiedPid;
        }
        catch (Exception ex)
        {
            log.Error($"JobKeeper: error during registration for {identity.TargetSid}: {ex.Message}");
            pipeServer.Dispose();
            KillExpectedKeeper(expectedPid);
            return 0;
        }
    }

    private bool WaitForConnection(NamedPipeServerStream pipeServer, TimeSpan timeout)
    {
        Exception? error = null;
        var connected = false;
        var waitThread = new Thread(() =>
        {
            try
            {
                pipeServer.WaitForConnection();
                connected = true;
            }
            catch (Exception ex)
            {
                error = ex;
            }
        })
        {
            IsBackground = true,
            Name = "RunFence JobKeeper pipe wait"
        };

        waitThread.Start();
        if (!waitThread.Join(timeout))
            return false;

        if (error != null)
            throw error;

        return connected;
    }

    public int TryReconnectExistingJobKeeper(string sid, bool isLow, SecurityIdentifier targetUserSid)
    {
        var identity = identityStore.Get(sid, isLow);
        if (identity == null)
            return 0;

        var keeperPid = processDiscovery.FindRunningJobKeeperPid(targetUserSid, isLow);
        if (keeperPid == null)
            return RejectPersistedIdentity(sid, isLow, "no matching keeper process was found");

        if (identity.LastVerifiedKeeperPid > 0 && identity.LastVerifiedKeeperPid != keeperPid.Value)
        {
            processTerminator.Kill(keeperPid.Value);
            return RejectPersistedIdentity(sid, isLow, "matching keeper process did not have the persisted PID");
        }

        log.Info($"JobKeeper: found existing keeper PID={keeperPid} for {sid} (isLow={isLow}), reconnecting");
        NamedPipeServerStream pipeServer;
        try
        {
            pipeServer = pipeServerFactory.Create(identity, targetUserSid);
        }
        catch (Exception ex)
        {
            log.Warn($"JobKeeper: failed to create reconnect pipe for {sid}: {ex.Message}");
            processTerminator.Kill(keeperPid.Value);
            return RejectPersistedIdentity(sid, isLow, "reconnect pipe creation failed");
        }

        var connectedPid = WaitAndRegisterJobKeeper(identity, pipeServer, keeperPid.Value, targetUserSid);
        if (connectedPid <= 0)
            return RejectPersistedIdentity(sid, isLow, "existing keeper failed verification or reconnect");

        return connectedPid;
    }

    public void RemoveJobKeeper(string sid, bool isLow) => registry.RemoveAndDispose(sid, isLow);

    private int RejectPersistedIdentity(string sid, bool isLow, string reason)
    {
        log.Warn($"JobKeeper: rejecting persisted identity for {sid} (isLow={isLow}): {reason}");
        identityStore.Remove(sid, isLow);
        return 0;
    }

    private void KillExpectedKeeper(int expectedPid)
    {
        if (expectedPid > 0)
            processTerminator.Kill(expectedPid);
    }
}
