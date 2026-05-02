using System.ComponentModel;
using System.Runtime.InteropServices;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launching.Environment;
using RunFence.Launching.Resolution;

namespace RunFence.Launch.Tokens;

public class CreateProcessLauncherHelper(
    ILoggingService log,
    IElevatedLinkedTokenProvider elevatedLinkedTokenProvider,
    ISaferDeElevationHelper saferDeElevationHelper,
    IProcessJobManager processJobManager,
    IJobKeeperService jobKeeperService,
    IRestrictedJobLaunchCoordinator restrictedJobLaunchCoordinator,
    IExecutablePathResolver executablePathResolver)
    : ICreateProcessLauncherHelper, IRestrictedJobProcessLauncher
{
    private static readonly string[] PrivilegesToDisable =
    [
        TokenPrivilegeHelper.SeBackupPrivilege,
        TokenPrivilegeHelper.SeRestorePrivilege,
        TokenPrivilegeHelper.SeTakeOwnershipPrivilege,
        TokenPrivilegeHelper.SeDebugPrivilege,
        TokenPrivilegeHelper.SeIncreaseQuotaPrivilege,
        TokenPrivilegeHelper.SeRelabelPrivilege,
    ];

    public ProcessInfo LaunchUsingAcquiredToken(IntPtr hToken, ProcessLaunchTarget psi, AccountLaunchIdentity identity)
    {
        var tokenSource = identity.Credentials!.Value.TokenSource;
        var privilegeLevel = identity.PrivilegeLevel!.Value;

        // Fast path: if a job keeper is already active for this SID/IL, bypass token preparation
        // and delegate directly to the keeper. The keeper inherits all token properties from when
        // it was seeded (DACL, privileges, IL) so children automatically get the right setup.
        bool useJobKeeper = privilegeLevel is PrivilegeLevel.Basic or PrivilegeLevel.LowIntegrity;
        bool isLow = privilegeLevel == PrivilegeLevel.LowIntegrity;
        if (useJobKeeper && jobKeeperService.HasJobKeeper(identity.Sid, isLow))
            return restrictedJobLaunchCoordinator.LaunchViaJobKeeper(identity.Sid, isLow, psi);

        IntPtr hDupToken = IntPtr.Zero;
        IntPtr hLinkedToken = IntPtr.Zero;
        IntPtr hRestrictedToken = IntPtr.Zero;
        IntPtr pIntegritySid = IntPtr.Zero;
        IntPtr tmlBuffer = IntPtr.Zero;

        try
        {
            ProcessLaunchNative.PROCESS_INFORMATION pi;

            switch (privilegeLevel)
            {
                case PrivilegeLevel.HighestAllowed:
                {
                    var sourceToken = hToken;
                    if (tokenSource != LaunchTokenSource.CurrentProcess && !TokenRestrictionHelper.IsTokenElevated(hToken))
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
                    pi = LaunchWithTokenCore(hDupToken, psi, tokenSource, identity.Sid);
                    break;
                }
                case PrivilegeLevel.Basic:
                case PrivilegeLevel.AboveBasic:
                case PrivilegeLevel.LowIntegrity:
                {
                    var integrityLabel = isLow ? "low" : "medium";

                    if (TokenRestrictionHelper.IsTokenElevated(hToken))
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
                            log.Info("LaunchUsingAcquiredToken: No linked token — will use SaferDeElevation");
                        }

                        log.Info("LaunchUsingAcquiredToken: DuplicateToken");
                        hDupToken = NativeTokenAcquisition.DuplicateToken(sourceToken);

                        var effectiveToken = hDupToken;
                        if (!hasLinkedToken)
                        {
                            log.Info($"LaunchUsingAcquiredToken: no linked token — using SaferDeElevation, IL={integrityLabel}");
                            hRestrictedToken = saferDeElevationHelper.CreateDeElevatedToken(hDupToken);
                            effectiveToken = hRestrictedToken;
                        }

                        log.Info($"LaunchUsingAcquiredToken: set integrity to {integrityLabel}");
                        if (isLow)
                            NativeTokenAcquisition.SetLowIntegrityOnToken(effectiveToken, out pIntegritySid, out tmlBuffer);
                        else
                            NativeTokenAcquisition.SetMediumIntegrityOnToken(effectiveToken, out pIntegritySid, out tmlBuffer);

                        if (useJobKeeper)
                        {
                            pi = restrictedJobLaunchCoordinator.SeedJobKeeperAndLaunch(effectiveToken, tokenSource, identity.Sid, isLow, psi);
                            return new ProcessInfo(pi);
                        }

                        pi = LaunchWithTokenCore(effectiveToken, psi, tokenSource, identity.Sid);
                    }
                    else
                    {
                        log.Info("LaunchUsingAcquiredToken: not elevated, DuplicateToken");
                        hDupToken = NativeTokenAcquisition.DuplicateToken(hToken);
                        if (isLow)
                        {
                            log.Info("LaunchUsingAcquiredToken: not elevated, set low integrity");
                            NativeTokenAcquisition.SetLowIntegrityOnToken(hDupToken, out pIntegritySid, out tmlBuffer);
                        }

                        if (useJobKeeper)
                        {
                            pi = restrictedJobLaunchCoordinator.SeedJobKeeperAndLaunch(hDupToken, tokenSource, identity.Sid, isLow, psi);
                            return new ProcessInfo(pi);
                        }

                        pi = LaunchWithTokenCore(hDupToken, psi, tokenSource, identity.Sid);
                    }
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(privilegeLevel), privilegeLevel, null);
            }

            if (pi.hProcess != IntPtr.Zero)
            {
                processJobManager.TryAssignToJob(identity.Sid, pi.hProcess, JobAssignment.Tracking);
                if (pi.hThread != IntPtr.Zero && ProcessLaunchNative.ResumeThread(pi.hThread) == uint.MaxValue)
                    log.Error($"ResumeThread failed for process {pi.dwProcessId}: error {Marshal.GetLastWin32Error()}");
            }
            return new ProcessInfo(pi);
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

    private ProcessLaunchNative.PROCESS_INFORMATION LaunchWithTokenCore(
        IntPtr hDupToken,
        ProcessLaunchTarget psi,
        LaunchTokenSource tokenSource,
        string accountSid,
        bool allowUnsuspendedRetry = true)
    {
        log.Info("LaunchWithTokenCore: AllowSetForegroundWindow");

        // Best-effort: if RunFence.exe currently holds foreground rights (e.g. its window is focused),
        // grant any process the right to set the foreground window. The primary grant for IPC-triggered
        // launches is made in RunFence.Launcher (which is created by the shell with foreground rights).
        ProcessLaunchNative.AllowSetForegroundWindow(ProcessLaunchNative.ASFW_ANY);

        if (tokenSource == LaunchTokenSource.CurrentProcess)
        {
            log.Info("LaunchWithTokenCore: DisablePrivilegesOnToken");
            TokenPrivilegeHelper.DisablePrivilegesOnToken(hDupToken, PrivilegesToDisable);
        }

        log.Info("LaunchWithTokenCore: SetRestrictiveDefaultDacl");
        NativeTokenAcquisition.SetRestrictiveDefaultDacl(hDupToken, accountSid);

        log.Info("LaunchWithTokenCore: BuildEnvironmentBlock");
        NativeEnvironmentBlock envBlock;
        if (ProcessLaunchNative.CreateEnvironmentBlock(out var pEnv, hDupToken, false))
            envBlock = new NativeEnvironmentBlock(pEnv, isOverridden: false);
        else
        {
            log.Warn("CreateEnvironmentBlock failed — process will inherit parent environment");
            envBlock = new NativeEnvironmentBlock();
        }

        try
        {
            envBlock.MergeInPlace(psi.EnvironmentVariables);

            // Resolve short exe names (e.g. "wt.exe") against the target user's PATH.
            // CreateProcessWithTokenW uses the calling process's PATH for module search,
            // not the environment block's PATH, so we must resolve beforehand.
            var resolutionContext = envBlock.Pointer != IntPtr.Zero
                ? ExecutablePathResolutionContext.TargetEnvironment(
                    new NativeEnvironmentVariableReader(envBlock.Pointer),
                    accountSid)
                : ExecutablePathResolutionContext.DirectOnly();
            var resolvedExePath = executablePathResolver.TryResolvePath(psi.ExePath, resolutionContext);
            if (resolvedExePath != null)
                psi = psi with { ExePath = resolvedExePath };

            try
            {
                return ProcessLaunchNative.CreateProcessWithToken(hDupToken, psi, envBlock.Pointer, log, suspended: true);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                // CREATE_BREAKAWAY_FROM_JOB failed — the parent job (PCA/seclogon) does not
                // allow breakaway. Retry without it; the process will remain in the parent job
                // and TryAssignToJob handles the nesting conflict via its fresh-job path.
                log.Warn("LaunchWithTokenCore: breakaway denied, retrying without CREATE_BREAKAWAY_FROM_JOB");
                try
                {
                    return ProcessLaunchNative.CreateProcessWithToken(hDupToken, psi, envBlock.Pointer, log, suspended: true, breakawayFromJob: false);
                }
                catch (Win32Exception ex2) when (ex2.NativeErrorCode == 5)
                {
                    if (!allowUnsuspendedRetry)
                        throw;

                    // CREATE_SUSPENDED fails when an existing restricted job with UI limits is active
                    // for this user (e.g. the medium keeper job already has JOB_OBJECT_UILIMIT_*).
                    // Retry without suspend — the process starts before job assignment, but the
                    // job keeper's pipe protocol ensures no launches are dispatched until after
                    // WaitAndRegisterJobKeeper (which runs after TryAssignToJob).
                    log.Warn("LaunchWithTokenCore: suspended launch denied (restricted job active), retrying without CREATE_SUSPENDED");
                    return ProcessLaunchNative.CreateProcessWithToken(hDupToken, psi, envBlock.Pointer, log, suspended: false, breakawayFromJob: false);
                }
            }
        }
        finally
        {
            envBlock.Dispose();
        }
    }

    public ProcessLaunchNative.PROCESS_INFORMATION LaunchWithPreparedToken(
        IntPtr token,
        ProcessLaunchTarget target,
        LaunchTokenSource tokenSource,
        string accountSid,
        bool allowUnsuspendedRetry = true) =>
        LaunchWithTokenCore(token, target, tokenSource, accountSid, allowUnsuspendedRetry);

    public (IntPtr Token, ProcessLaunchNative.PROCESS_INFORMATION TempProcess) AcquireBootstrapToken(AccountLaunchIdentity identity)
    {
        log.Info("AcquireBootstrapToken: CreateProcessWithLogonW");

        var pi = ProcessLaunchNative.CreateProcessWithLogon(new ProcessLaunchTarget("cmd.exe", "/c timeout /t 10 /nobreak >nul", HideWindow: true), identity.Credentials!.Value, log);

        try
        {
            log.Info("AcquireBootstrapToken: OpenProcessToken");
            if (!ProcessNative.OpenProcessToken(
                    pi.hProcess,
                    ProcessLaunchNative.TOKEN_DUPLICATE | ProcessLaunchNative.TOKEN_QUERY | ProcessLaunchNative.TOKEN_IMPERSONATE,
                    out var hTempToken))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                log.Info("AcquireBootstrapToken: DuplicateToken");
                return (NativeTokenAcquisition.DuplicateToken(hTempToken), pi);
            }
            finally
            {
                ProcessNative.CloseHandle(hTempToken);
            }
        }
        catch
        {
            ProcessLaunchNative.TerminateProcess(pi.hProcess, 1);
            ProcessNative.CloseHandle(pi.hProcess);
            ProcessNative.CloseHandle(pi.hThread);
            throw;
        }
    }
}
