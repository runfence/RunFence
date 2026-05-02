using System.Security.Principal;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Launch.Tokens;

public sealed class RestrictedJobLaunchCoordinator(
    ILoggingService log,
    IProcessJobManager processJobManager,
    IJobKeeperService jobKeeperService,
    IJobKeeperIdentityStore jobKeeperIdentityStore,
    IJobKeeperPipeServerFactory pipeServerFactory,
    IJobKeeperLaunchIpcClient launchIpcClient,
    IJobObjectApi jobObjectApi,
    RestrictedProcessActivationGuard restrictedProcessGuard,
    IJobKeeperLaunchProcessApi launchProcessApi,
    string jobKeeperExePath)
    : IRestrictedJobLaunchCoordinator, IRestrictedJobLaunchCoordinatorInitializer
{
    private IRestrictedJobProcessLauncher? _processLauncher;

    public void Initialize(IRestrictedJobProcessLauncher processLauncher)
    {
        if (_processLauncher != null)
            throw new InvalidOperationException("Restricted job launch coordinator is already initialized.");

        _processLauncher = processLauncher;
    }

    public ProcessLaunchNative.PROCESS_INFORMATION SeedJobKeeperAndLaunch(
        IntPtr hToken,
        LaunchTokenSource tokenSource,
        string sid,
        bool isLow,
        ProcessLaunchTarget psi)
    {
        var processLauncher = _processLauncher
                              ?? throw new InvalidOperationException("Restricted job launch coordinator is not initialized.");
        var targetSid = new SecurityIdentifier(sid);
        var jobAssignment = isLow ? JobAssignment.LowIntegrity : JobAssignment.Restricted;

        int reconnectedPid = jobKeeperService.TryReconnectExistingJobKeeper(sid, isLow, targetSid);
        if (reconnectedPid > 0)
        {
            log.Info($"JobKeeper: reconnected to existing keeper PID={reconnectedPid} for {sid} (isLow={isLow})");
            return LaunchViaJobKeeperCore(sid, isLow, psi);
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var identity = jobKeeperIdentityStore.CreateFresh(sid, isLow);
            System.IO.Pipes.NamedPipeServerStream? pipeServer = null;
            ProcessLaunchNative.PROCESS_INFORMATION jobKeeperPi = default;
            var removeIdentityOnFailure = true;
            var restrictedJobAssigned = false;

            try
            {
                pipeServer = pipeServerFactory.Create(identity, targetSid);
                processJobManager.ResetJobHandle(sid, jobAssignment);

                var jobKeeperTarget = new ProcessLaunchTarget(
                    jobKeeperExePath,
                    BuildJobKeeperArguments(identity),
                    HideWindow: true);

                jobKeeperPi = processLauncher.LaunchWithPreparedToken(
                    hToken,
                    jobKeeperTarget,
                    tokenSource,
                    sid,
                    allowUnsuspendedRetry: false);

                var assignment = processJobManager.TryAssignToJob(sid, jobKeeperPi.hProcess, jobAssignment, identity.JobName);
                if (!assignment.Succeeded)
                {
                    restrictedProcessGuard.TerminateAndClose(ref jobKeeperPi);
                    pipeServer.Dispose();
                    pipeServer = null;
                    jobKeeperIdentityStore.Remove(sid, isLow);

                    if (assignment.FailureKind == JobAssignmentFailureKind.PreexistingNamedJobRejected)
                        continue;

                    throw RestrictedProcessActivationGuard.RestrictedJobAssignmentFailed(sid, isLow, assignment.FailureReason);
                }
                restrictedJobAssigned = true;

                if (!jobObjectApi.DuplicateHandleToProcess(
                        ProcessNative.GetCurrentProcess(),
                        assignment.AssignedJobHandle,
                        jobKeeperPi.hProcess,
                        ProcessJobManager.JobObjectKeepAliveAccess))
                {
                    var error = jobObjectApi.GetLastWin32Error();
                    restrictedProcessGuard.TerminateAndClose(ref jobKeeperPi);
                    pipeServer.Dispose();
                    pipeServer = null;
                    jobKeeperIdentityStore.Remove(sid, isLow);
                    throw RestrictedProcessActivationGuard.RestrictedJobAssignmentFailed(sid, isLow,
                        $"Failed to duplicate keeper job keep-alive handle: Win32 error {error}.");
                }

                restrictedProcessGuard.ResumeOrTerminate(ref jobKeeperPi, sid, isLow, "job keeper");
                var jobKeeperPid = (int)jobKeeperPi.dwProcessId;
                restrictedProcessGuard.CloseThreadHandle(ref jobKeeperPi);

                int keeperPid = jobKeeperService.WaitAndRegisterJobKeeper(identity, pipeServer, jobKeeperPid, targetSid);
                if (keeperPid > 0)
                {
                    restrictedProcessGuard.CloseHandles(ref jobKeeperPi);
                    pipeServer = null;
                    removeIdentityOnFailure = false;
                    return LaunchViaJobKeeperCore(sid, isLow, psi);
                }

                pipeServer = null;
                restrictedProcessGuard.CloseHandles(ref jobKeeperPi);

                log.Error($"JobKeeper: seeding failed for {sid} (isLow={isLow}); restricted launch fails closed");
                throw RestrictedProcessActivationGuard.RestrictedJobAssignmentFailed(
                    sid,
                    isLow,
                    "JobKeeper failed to register after the restricted job was created.");
            }
            catch
            {
                if (jobKeeperPi.hProcess != IntPtr.Zero || jobKeeperPi.hThread != IntPtr.Zero)
                    restrictedProcessGuard.TerminateAndClose(ref jobKeeperPi);

                pipeServer?.Dispose();

                if (restrictedJobAssigned)
                    processJobManager.ResetJobHandle(sid, jobAssignment);

                if (removeIdentityOnFailure)
                    jobKeeperIdentityStore.Remove(sid, isLow);

                throw;
            }
        }

        throw RestrictedProcessActivationGuard.RestrictedJobAssignmentFailed(sid, isLow, "Could not create a non-preexisting named restricted job.");
    }

    public ProcessInfo LaunchViaJobKeeper(string sid, bool isLow, ProcessLaunchTarget psi) =>
        new(LaunchViaJobKeeperCore(sid, isLow, psi));

    public ProcessLaunchNative.PROCESS_INFORMATION LaunchViaJobKeeperCore(
        string sid,
        bool isLow,
        ProcessLaunchTarget psi)
    {
        launchProcessApi.AllowAnyForegroundWindow();

        var request = new JobKeeperLaunchRequest(
            psi.ExePath,
            psi.Arguments,
            psi.WorkingDirectory,
            psi.HideWindow,
            psi.EnvironmentVariables);

        int pid = launchIpcClient.SendLaunchRequest(sid, isLow, request);
        if (pid <= 0)
            throw new InvalidOperationException($"Job keeper failed to launch process for {sid}");

        var hProcess = launchProcessApi.OpenLaunchedProcess(pid);
        return new ProcessLaunchNative.PROCESS_INFORMATION { hProcess = hProcess, dwProcessId = (uint)pid };
    }

    private static string BuildJobKeeperArguments(JobKeeperInstanceIdentity identity) =>
        $"--pipe \"{identity.PipeName}\"";
}
