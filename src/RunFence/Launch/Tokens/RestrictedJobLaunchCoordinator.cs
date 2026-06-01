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
    IPreparedTokenProcessLauncher preparedTokenProcessLauncher,
    string jobKeeperExePath)
    : IRestrictedJobLaunchCoordinator
{
    private static readonly TimeSpan JobKeeperLaunchTimeout = TimeSpan.FromMilliseconds(IpcConstants.JobKeeperLaunchIpcTimeoutMs);

    public ProcessLaunchNative.PROCESS_INFORMATION SeedJobKeeperAndLaunch(
        IntPtr hToken,
        LaunchTokenSource tokenSource,
        string sid,
        bool isLow,
        ProcessLaunchTarget psi)
    {
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
            var localJobName = BuildLocalJobName(isLow);
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
                    HideWindow: true,
                    SuppressStartupFeedback: true);

                jobKeeperPi = preparedTokenProcessLauncher.LaunchWithPreparedToken(
                    hToken,
                    jobKeeperTarget,
                    tokenSource,
                    sid,
                    allowUnsuspendedRetry: false);

                var assignment = processJobManager.TryAssignToJob(sid, jobKeeperPi.hProcess, jobAssignment, localJobName);
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
                        ProcessJobManager.JobObjectKeepAliveAccess,
                        out _))
                {
                    var error = jobObjectApi.GetLastWin32Error();
                    throw RestrictedProcessActivationGuard.RestrictedJobAssignmentFailed(sid, isLow,
                        $"Failed to duplicate keeper job keep-alive handle: Win32 error {error}.");
                }

                if (!jobObjectApi.DuplicateHandleToProcess(
                        ProcessNative.GetCurrentProcess(),
                        assignment.AssignedJobHandle,
                        jobKeeperPi.hProcess,
                        ProcessJobManager.JobObjectReconnectAccess,
                        out _))
                {
                    var error = jobObjectApi.GetLastWin32Error();
                    throw RestrictedProcessActivationGuard.RestrictedJobAssignmentFailed(sid, isLow,
                        $"Failed to duplicate keeper reconnect discovery handle: Win32 error {error}.");
                }

                restrictedProcessGuard.ResumeOrTerminate(ref jobKeeperPi, sid, isLow, "job keeper");
                var jobKeeperPid = (int)jobKeeperPi.dwProcessId;
                restrictedProcessGuard.CloseThreadHandle(ref jobKeeperPi);

                int keeperPid = jobKeeperService.WaitAndRegisterJobKeeper(identity, pipeServer, jobKeeperPid, targetSid, jobKeeperPi.hProcess);
                if (keeperPid > 0)
                {
                    restrictedProcessGuard.CloseHandles(ref jobKeeperPi);
                    pipeServer = null;
                    removeIdentityOnFailure = false;
                    return LaunchViaJobKeeperCore(sid, isLow, psi);
                }

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
        log.Info(
            $"JobKeeper: sending launch request for {sid} (isLow={isLow}), target='{psi.ExePath}', args='{psi.Arguments ?? string.Empty}'");
        launchProcessApi.AllowAnyForegroundWindow();

        var request = new JobKeeperLaunchRequest(
            psi.ExePath,
            psi.Arguments,
            psi.WorkingDirectory,
            psi.HideWindow,
            psi.SuppressStartupFeedback,
            psi.EnvironmentVariables);

        var launchedProcess = launchIpcClient
            .SendLaunchRequestAsync(sid, isLow, request, JobKeeperLaunchTimeout, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        if (launchedProcess == null || launchedProcess.Value.Pid <= 0)
            throw new StaleJobKeeperException(sid);

        var hProcess = launchedProcess.Value.ProcessHandleValue != 0
            ? new IntPtr(launchedProcess.Value.ProcessHandleValue)
            : launchProcessApi.OpenLaunchedProcess(launchedProcess.Value.Pid);
        return new ProcessLaunchNative.PROCESS_INFORMATION { hProcess = hProcess, dwProcessId = (uint)launchedProcess.Value.Pid };
    }

    private static string BuildJobKeeperArguments(JobKeeperInstanceIdentity identity) =>
        $"--pipe \"{identity.PipeName}\"";

    private static string BuildLocalJobName(bool isLow) =>
        $@"Global\RunFence_JK_{(isLow ? "L" : "R")}_{Guid.NewGuid():N}";
}
