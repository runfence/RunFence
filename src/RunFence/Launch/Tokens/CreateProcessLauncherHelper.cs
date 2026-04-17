using System.ComponentModel;
using System.Runtime.InteropServices;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Launch.Tokens;

public class CreateProcessLauncherHelper(ILoggingService log, ElevatedLinkedTokenProvider elevatedLinkedTokenProvider, SaferDeElevationHelper saferDeElevationHelper)
{
    private static readonly string[] PrivilegesToDisable =
    [
        TokenPrivilegeHelper.SeBackupPrivilege,
        TokenPrivilegeHelper.SeRestorePrivilege,
        TokenPrivilegeHelper.SeTakeOwnershipPrivilege,
        TokenPrivilegeHelper.SeDebugPrivilege,
        TokenPrivilegeHelper.SeIncreaseQuotaPrivilege,
    ];

    public ProcessInfo LaunchUsingAcquiredToken(IntPtr hToken, ProcessLaunchTarget psi, AccountLaunchIdentity identity)
    {
        var tokenSource = identity.Credentials!.Value.TokenSource;
        var privilegeLevel = identity.PrivilegeLevel!.Value;

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
                    pi = LaunchWithTokenCore(hDupToken, psi, tokenSource, identity.Sid);
                    break;
                }
                case PrivilegeLevel.Basic:
                case PrivilegeLevel.LowIntegrity:
                {
                    var setLowIntegrity = privilegeLevel == PrivilegeLevel.LowIntegrity;
                    var integrityLabel = setLowIntegrity ? "low" : "medium";

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
                        if (setLowIntegrity)
                            NativeTokenAcquisition.SetLowIntegrityOnToken(effectiveToken, out pIntegritySid, out tmlBuffer);
                        else
                            NativeTokenAcquisition.SetMediumIntegrityOnToken(effectiveToken, out pIntegritySid, out tmlBuffer);
                        pi = LaunchWithTokenCore(effectiveToken, psi, tokenSource, identity.Sid);
                    }
                    else
                    {
                        log.Info("LaunchUsingAcquiredToken: not elevated, DuplicateToken");
                        hDupToken = NativeTokenAcquisition.DuplicateToken(hToken);
                        if (setLowIntegrity)
                        {
                            log.Info("LaunchUsingAcquiredToken: not elevated, set low integrity");
                            NativeTokenAcquisition.SetLowIntegrityOnToken(hDupToken, out pIntegritySid, out tmlBuffer);
                        }
                        pi = LaunchWithTokenCore(hDupToken, psi, tokenSource, identity.Sid);
                    }
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(privilegeLevel), privilegeLevel, null);
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

    private ProcessLaunchNative.PROCESS_INFORMATION LaunchWithTokenCore(IntPtr hDupToken, ProcessLaunchTarget psi, LaunchTokenSource tokenSource, string accountSid)
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
            return ProcessLaunchNative.CreateProcessWithToken(hDupToken, psi, envBlock.Pointer, log);
        }
        finally
        {
            envBlock.Dispose();
        }
    }

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