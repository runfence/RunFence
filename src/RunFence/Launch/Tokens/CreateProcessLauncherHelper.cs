using System.ComponentModel;
using System.Runtime.InteropServices;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Launch.Tokens;

public class CreateProcessLauncherHelper(
    ILoggingService log,
    IElevatedLinkedTokenProvider elevatedLinkedTokenProvider,
    ISaferDeElevationHelper saferDeElevationHelper,
    ITokenPrivilegeStateReader tokenPrivilegeStateReader,
    ITokenIntegrityLevelService tokenIntegrityLevelService,
    IProcessJobManager processJobManager,
    IProcessControl processControl,
    Func<ITrackingJobStateStore> trackingJobStateStoreFactory,
    IJobKeeperService jobKeeperService,
    IRestrictedJobLaunchCoordinator restrictedJobLaunchCoordinator,
    IPreparedTokenProcessLauncher preparedTokenProcessLauncher,
    IProfileKeeperBootstrapContext profileKeeperBootstrapContext,
    string profileKeeperExePath)
    : ICreateProcessLauncherHelper
{
    public ProcessInfo? LaunchUsingAcquiredToken(IntPtr hToken, ProcessLaunchTarget psi, AccountLaunchIdentity identity)
    {
        var tokenSource = identity.Credentials!.Value.TokenSource;
        var privilegeLevel = identity.PrivilegeLevel!.Value;

        // Fast path: if a job keeper is already active for this SID/IL, bypass token preparation
        // and delegate directly to the keeper. The keeper inherits all token properties from when
        // it was seeded (DACL, privileges, IL) so children automatically get the right setup.
        bool useJobKeeper = privilegeLevel is PrivilegeLevel.Isolated or PrivilegeLevel.LowIntegrity;
        bool isLow = privilegeLevel == PrivilegeLevel.LowIntegrity;
        bool isHighIntegrity = privilegeLevel == PrivilegeLevel.HighIntegrity;
        if (useJobKeeper && jobKeeperService.HasJobKeeper(identity.Sid, isLow))
        {
            try
            {
                log.Info(
                    $"LaunchUsingAcquiredToken: launching via existing JobKeeper for {identity.Sid} (isLow={isLow}), target='{psi.ExePath}', args='{psi.Arguments ?? string.Empty}'");
                return restrictedJobLaunchCoordinator.LaunchViaJobKeeper(identity.Sid, isLow, psi);
            }
            catch (StaleJobKeeperException ex) when (string.Equals(ex.Sid, identity.Sid, StringComparison.Ordinal))
            {
                log.Warn(
                    $"LaunchUsingAcquiredToken: existing JobKeeper fast path failed for {identity.Sid} (isLow={isLow}), reseeding in same attempt: {ex.Message}");
            }
        }

        IntPtr hDupToken = IntPtr.Zero;
        IntPtr hLinkedToken = IntPtr.Zero;
        IntPtr hRestrictedToken = IntPtr.Zero;
        IntPtr pIntegritySid = IntPtr.Zero;
        IntPtr tmlBuffer = IntPtr.Zero;
        ProcessLaunchNative.PROCESS_INFORMATION pi = default;

        try
        {
            switch (privilegeLevel)
            {
                case PrivilegeLevel.HighestAllowed:
                {
                    var sourceToken = hToken;
                    var sourceTokenIsElevated = tokenPrivilegeStateReader.IsElevated(hToken);
                    if (tokenSource != LaunchTokenSource.CurrentProcess && !sourceTokenIsElevated)
                    {
                        var hProbeToken = LinkedTokenHelper.TryGetLinkedToken(hToken);
                        if (hProbeToken != IntPtr.Zero)
                        {
                            ProcessNative.CloseHandle(hProbeToken);
                            log.Info("LaunchUsingAcquiredToken: token not elevated but user is admin, acquiring elevated linked token via SYSTEM impersonation");
                            try
                            {
                                hLinkedToken = elevatedLinkedTokenProvider.AcquireElevatedLinkedToken(hToken);
                                sourceToken = hLinkedToken;
                                sourceTokenIsElevated = tokenPrivilegeStateReader.IsElevated(sourceToken);
                                log.Info("LaunchUsingAcquiredToken: using elevated linked token");
                            }
                            catch (Exception ex)
                            {
                                log.Warn($"LaunchUsingAcquiredToken: elevated linked token acquisition failed, launching non-elevated: {ex.Message}");
                            }
                        }
                        else
                        {
                            log.Info("LaunchUsingAcquiredToken: token not elevated and user is not admin, launching as-is");
                        }
                    }

                    log.Info("LaunchUsingAcquiredToken: DuplicateToken");
                    hDupToken = NativeTokenAcquisition.DuplicateToken(sourceToken);
                    if (SidResolutionHelper.IsSystemSid(identity.Sid))
                    {
                        log.Info("LaunchUsingAcquiredToken: EnableAllPresentPrivilegesOnToken for SYSTEM");
                        TokenPrivilegeHelper.EnableAllPresentPrivilegesOnToken(hDupToken);
                    }

                    if (!sourceTokenIsElevated
                        || !tokenPrivilegeStateReader.TryGetIntegrityLevel(hDupToken, out var currentIntegrityLevel)
                        || currentIntegrityLevel < NativeTokenHelper.MandatoryLevelHigh)
                    {
                        log.Info("LaunchUsingAcquiredToken: set integrity to high");
                        tokenIntegrityLevelService.SetHighIntegrity(hDupToken, out pIntegritySid, out tmlBuffer);
                    }
                    else
                    {
                        log.Info("LaunchUsingAcquiredToken: token already high integrity");
                    }

                    pi = preparedTokenProcessLauncher.LaunchWithPreparedToken(hDupToken, psi, tokenSource, identity.Sid);
                    break;
                }
                case PrivilegeLevel.Isolated:
                case PrivilegeLevel.HighIntegrity:
                case PrivilegeLevel.Basic:
                case PrivilegeLevel.LowIntegrity:
                {
                    var integrityLabel = isLow
                        ? "low"
                        : isHighIntegrity
                            ? "high"
                            : "medium";

                    if (tokenPrivilegeStateReader.IsElevated(hToken))
                    {
                        log.Info("LaunchUsingAcquiredToken: IsTokenElevated");

                        var sourceToken = hToken;
                        var hasLinkedToken = false;

                        hLinkedToken = LinkedTokenHelper.TryGetLinkedToken(hToken);
                        if (hLinkedToken != IntPtr.Zero)
                        {
                            sourceToken = hLinkedToken;
                            hasLinkedToken = true;
                            log.Info("LaunchUsingAcquiredToken: Using linked (filtered) token for de-elevation");
                        }
                        else
                        {
                            log.Info("LaunchUsingAcquiredToken: No linked token - will use SaferDeElevation");
                        }

                        log.Info("LaunchUsingAcquiredToken: DuplicateToken");
                        hDupToken = NativeTokenAcquisition.DuplicateToken(sourceToken);

                        var effectiveToken = hDupToken;
                        if (!hasLinkedToken)
                        {
                            log.Info($"LaunchUsingAcquiredToken: no linked token - using SaferDeElevation, IL={integrityLabel}");
                            hRestrictedToken = saferDeElevationHelper.CreateDeElevatedToken(hDupToken);
                            effectiveToken = hRestrictedToken;
                        }

                        log.Info($"LaunchUsingAcquiredToken: set integrity to {integrityLabel}");
                        if (isLow)
                            tokenIntegrityLevelService.SetLowIntegrity(effectiveToken, out pIntegritySid, out tmlBuffer);
                        else if (isHighIntegrity)
                            tokenIntegrityLevelService.SetHighIntegrity(effectiveToken, out pIntegritySid, out tmlBuffer);
                        else
                            tokenIntegrityLevelService.SetMediumIntegrity(effectiveToken, out pIntegritySid, out tmlBuffer);

                        if (useJobKeeper)
                        {
                            log.Info(
                                $"LaunchUsingAcquiredToken: launching via seeded JobKeeper for {identity.Sid} (isLow={isLow}), target='{psi.ExePath}', args='{psi.Arguments ?? string.Empty}'");
                            pi = restrictedJobLaunchCoordinator.SeedJobKeeperAndLaunch(effectiveToken, tokenSource, identity.Sid, isLow, psi);
                            var keeperProcessInfo = new ProcessInfo(pi);
                            pi = default;
                            return keeperProcessInfo;
                        }

                        pi = preparedTokenProcessLauncher.LaunchWithPreparedToken(effectiveToken, psi, tokenSource, identity.Sid);
                    }
                    else
                    {
                        log.Info("LaunchUsingAcquiredToken: not elevated, DuplicateToken");
                        hDupToken = NativeTokenAcquisition.DuplicateToken(hToken);
                        if (isLow)
                        {
                            log.Info("LaunchUsingAcquiredToken: not elevated, set low integrity");
                            tokenIntegrityLevelService.SetLowIntegrity(hDupToken, out pIntegritySid, out tmlBuffer);
                        }
                        else if (isHighIntegrity)
                        {
                            log.Info("LaunchUsingAcquiredToken: not elevated, set high integrity");
                            tokenIntegrityLevelService.SetHighIntegrity(hDupToken, out pIntegritySid, out tmlBuffer);
                        }

                        if (useJobKeeper)
                        {
                            log.Info(
                                $"LaunchUsingAcquiredToken: launching via seeded JobKeeper for {identity.Sid} (isLow={isLow}), target='{psi.ExePath}', args='{psi.Arguments ?? string.Empty}'");
                            pi = restrictedJobLaunchCoordinator.SeedJobKeeperAndLaunch(hDupToken, tokenSource, identity.Sid, isLow, psi);
                            var keeperProcessInfo = new ProcessInfo(pi);
                            pi = default;
                            return keeperProcessInfo;
                        }

                        pi = preparedTokenProcessLauncher.LaunchWithPreparedToken(hDupToken, psi, tokenSource, identity.Sid);
                    }
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(privilegeLevel), privilegeLevel, null);
            }

            if (pi.hProcess != IntPtr.Zero)
            {
                var trackingAssignment = processJobManager.TryAssignToJob(identity.Sid, pi.hProcess, JobAssignment.Tracking);
                if (trackingAssignment.Succeeded && trackingAssignment.AssignedKind == JobAssignment.Tracking)
                    trackingJobStateStoreFactory().AddTrackingJobSid(identity.Sid);

                if (pi.hThread != IntPtr.Zero && !processControl.ResumeThread(pi.hThread, out var error))
                    log.Error($"ResumeThread failed for process {pi.dwProcessId}: error {error}");
            }

            var launchedProcessInfo = new ProcessInfo(pi);
            pi = default;
            return launchedProcessInfo;
        }
        catch
        {
            CleanupOwnedProcessInformation(ref pi);
            throw;
        }
        finally
        {
            if (tmlBuffer != IntPtr.Zero)
                Marshal.FreeHGlobal(tmlBuffer);
            if (pIntegritySid != IntPtr.Zero)
                ProcessNative.LocalFree(pIntegritySid);
            if (hRestrictedToken != IntPtr.Zero)
                ProcessNative.CloseHandle(hRestrictedToken);
            if (hDupToken != IntPtr.Zero)
                ProcessNative.CloseHandle(hDupToken);
            if (hLinkedToken != IntPtr.Zero)
                ProcessNative.CloseHandle(hLinkedToken);
        }
    }

    public IntPtr AcquireProfileKeeperToken(AccountLaunchIdentity identity)
    {
        return profileKeeperBootstrapContext.Run(() =>
        {
            log.Info("AcquireProfileKeeperToken: CreateProcessWithLogonW");

            var pi = ProcessLaunchNative.CreateProcessWithLogon(
                new ProcessLaunchTarget(
                    profileKeeperExePath,
                    WorkingDirectory: AppContext.BaseDirectory,
                    HideWindow: true,
                    SuppressStartupFeedback: true),
                identity.Credentials!.Value,
                log);

            try
            {
                log.Info("AcquireProfileKeeperToken: OpenProcessToken");
                if (!ProcessNative.OpenProcessToken(
                        pi.hProcess,
                        ProcessLaunchNative.TOKEN_DUPLICATE | ProcessLaunchNative.TOKEN_QUERY | ProcessLaunchNative.TOKEN_IMPERSONATE,
                        out var hTempToken))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                try
                {
                    log.Info("AcquireProfileKeeperToken: DuplicateToken");
                    return NativeTokenAcquisition.DuplicateToken(hTempToken);
                }
                finally
                {
                    ProcessNative.CloseHandle(hTempToken);
                }
            }
            catch
            {
                ProcessLaunchNative.TerminateProcess(pi.hProcess, 1);
                throw;
            }
            finally
            {
                if (pi.hThread != IntPtr.Zero)
                    ProcessNative.CloseHandle(pi.hThread);
                if (pi.hProcess != IntPtr.Zero)
                    ProcessNative.CloseHandle(pi.hProcess);
            }
        });
    }

    private void CleanupOwnedProcessInformation(ref ProcessLaunchNative.PROCESS_INFORMATION pi)
    {
        if (pi.hProcess != IntPtr.Zero)
            processControl.TerminateProcessBestEffort(pi.hProcess, 1);

        if (pi.hThread != IntPtr.Zero)
        {
            processControl.CloseHandle(pi.hThread);
            pi.hThread = IntPtr.Zero;
        }

        if (pi.hProcess != IntPtr.Zero)
        {
            processControl.CloseHandle(pi.hProcess);
            pi.hProcess = IntPtr.Zero;
        }
    }
}
