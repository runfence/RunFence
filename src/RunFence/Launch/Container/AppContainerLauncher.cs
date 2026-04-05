using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Tokens;

namespace RunFence.Launch.Container;

/// <summary>
/// Launches a process inside a Windows AppContainer.
///
/// Strategy: Get the interactive user's token from explorer.exe, create an AppContainer
/// token via CreateAppContainerToken (kernelbase.dll), then launch via CreateProcessWithTokenW.
///
/// - CreateAppContainerToken creates an AppContainer token from any existing token, automatically
///   setting up the AppContainerNamedObjects kernel directory. This is critical — without the
///   named object namespace, processes can't create named mutexes/events/shared memory and most
///   Win32 apps silently fail. (Chromium migrated from NtCreateLowBoxToken to this API for the
///   same reason.)
/// - CreateProcessWithTokenW only needs SE_IMPERSONATE_NAME (present in admin tokens).
///   It uses STARTUPINFO (not STARTUPINFOEX), so no PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES needed.
/// - The OS automatically sets Low IL on AppContainer tokens.
/// </summary>
public static class AppContainerLauncher
{
    private const uint SE_GROUP_ENABLED = 0x00000004;

    [DllImport("kernelbase.dll", SetLastError = true)]
    private static extern bool CreateAppContainerToken(
        IntPtr TokenHandle,
        ref SECURITY_CAPABILITIES SecurityCapabilities,
        out IntPtr OutToken);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetAppContainerNamedObjectPath(
        IntPtr Token, IntPtr AppContainerSid, uint ObjectPathLength,
        StringBuilder ObjectPath, out uint ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_CAPABILITIES
    {
        public IntPtr AppContainerSid;
        public IntPtr Capabilities;
        public uint CapabilityCount;
        public uint Reserved;
    }

    public static void Launch(ProcessStartInfo psi,
        AppContainerEntry entry, ILoggingService log,
        IAppContainerEnvironmentSetup environmentSetup,
        AppContainerProfileSetup appContainerProfileSetup,
        Dictionary<string, string>? extraEnvVars = null)
    {
        log.Info($"AppContainerLauncher: Starting launch of '{psi.FileName}' in container '{entry.Name}'");

        IntPtr hExplorerToken = IntPtr.Zero;
        IntPtr hDupToken = IntPtr.Zero;
        IntPtr hAppContainerToken = IntPtr.Zero;
        IntPtr pContainerSid = IntPtr.Zero;
        IntPtr pCapSids = IntPtr.Zero;
        var envBlock = new NativeEnvironmentBlock();
        var capSidPtrs = new List<IntPtr>();

        try
        {
            // Step 1: Get the interactive user's token from explorer.exe
            hExplorerToken = ExplorerTokenHelper.GetSessionExplorerToken(log);
            log.Info("AppContainerLauncher: [1/5] Acquired interactive user token");

            // Step 2: Ensure AppContainer profile exists under the interactive user's HKCU
            appContainerProfileSetup.EnsureProfileUnderToken(entry, hExplorerToken);

            // Step 3: Derive AppContainer SID
            var deriveHr = AppContainerNative.DeriveAppContainerSidFromAppContainerName(entry.Name, out pContainerSid);
            if (deriveHr != 0 || pContainerSid == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"Failed to derive AppContainer SID for '{entry.Name}'. HRESULT: 0x{deriveHr:X8}");

            if (AppContainerNative.ConvertSidToStringSid(pContainerSid, out var pSidStr))
            {
                string? sidStrLog;
                try
                {
                    sidStrLog = Marshal.PtrToStringUni(pSidStr);
                }
                finally
                {
                    NativeMethods.LocalFree(pSidStr);
                }

                log.Info($"AppContainerLauncher: [2/5] Derived AppContainer SID: {sidStrLog}");
            }

            // Step 4: Build capability SID array
            var capCount = 0u;
            if (entry.Capabilities is { Count: > 0 })
            {
                foreach (var capSidStr in entry.Capabilities)
                {
                    if (!NativeMethods.ConvertStringSidToSid(capSidStr, out var capSid))
                    {
                        log.Warn($"AppContainerLauncher: Could not convert capability SID '{capSidStr}', skipping");
                        continue;
                    }

                    capSidPtrs.Add(capSid);
                }

                if (capSidPtrs.Count > 0)
                {
                    capCount = (uint)capSidPtrs.Count;
                    var capArraySize = Marshal.SizeOf<ProcessLaunchNative.SID_AND_ATTRIBUTES>() * capSidPtrs.Count;
                    pCapSids = Marshal.AllocHGlobal(capArraySize);
                    for (int i = 0; i < capSidPtrs.Count; i++)
                    {
                        var item = new ProcessLaunchNative.SID_AND_ATTRIBUTES
                        {
                            Sid = capSidPtrs[i],
                            Attributes = SE_GROUP_ENABLED
                        };
                        Marshal.StructureToPtr(item, IntPtr.Add(pCapSids,
                            i * Marshal.SizeOf<ProcessLaunchNative.SID_AND_ATTRIBUTES>()), false);
                    }
                }
            }

            log.Info($"AppContainerLauncher: Capability count: {capCount}");

            // Step 5: Create AppContainer token via CreateAppContainerToken (kernelbase.dll).
            // This API automatically sets up the AppContainerNamedObjects kernel directory,
            // which is critical for named mutexes/events/shared memory used by most Win32 apps.
            hDupToken = NativeTokenAcquisition.DuplicateToken(hExplorerToken);

            var secCaps = new SECURITY_CAPABILITIES
            {
                AppContainerSid = pContainerSid,
                Capabilities = pCapSids,
                CapabilityCount = capCount,
                Reserved = 0
            };
            if (!CreateAppContainerToken(hDupToken, ref secCaps, out hAppContainerToken))
            {
                var err = Marshal.GetLastWin32Error();
                log.Error($"AppContainerLauncher: CreateAppContainerToken failed — Win32 error {err} (0x{err:X8}): {new Win32Exception(err).Message}");
                throw new Win32Exception(err, "CreateAppContainerToken failed");
            }

            log.Info("AppContainerLauncher: [3/5] Created AppContainer token via CreateAppContainerToken");

            // Explicitly enable UAC file virtualization on the AppContainer token so that
            // legacy 32-bit apps without a manifest can redirect writes to VirtualStore.
            appContainerProfileSetup.TryEnableVirtualization(hAppContainerToken);

            // Verify named object namespace was set up (diagnostic)
            try
            {
                var pathBuf = new StringBuilder(512);
                if (GetAppContainerNamedObjectPath(hAppContainerToken, IntPtr.Zero, (uint)pathBuf.Capacity, pathBuf, out _))
                    log.Info($"AppContainerLauncher: Named object path: {pathBuf}");
                else
                    log.Warn($"AppContainerLauncher: GetAppContainerNamedObjectPath failed (error {Marshal.GetLastWin32Error()}) — named objects may not work");
            }
            catch (Exception ex)
            {
                log.Warn($"AppContainerLauncher: Named object path check failed: {ex.Message}");
            }

            // Step 6: Create environment block with AppContainer profile overrides.
            envBlock = PrepareEnvironmentBlock(
                hExplorerToken, entry, pContainerSid, psi.FileName, log, environmentSetup);

            envBlock.MergeInPlace(extraEnvVars);

            log.Info("AppContainerLauncher: [4/5] Built environment block with profile overrides");

            // Step 7: Launch via CreateProcessWithTokenW (needs SE_IMPERSONATE_NAME, which admin tokens have).
            // Uses STARTUPINFO (not STARTUPINFOEX) — no PROC_THREAD_ATTRIBUTE needed because
            // the token is already an AppContainer token created by CreateAppContainerToken.
            var cmdLine = ProcessLaunchNative.BuildCommandLine(psi);
            var workDir = string.IsNullOrEmpty(psi.WorkingDirectory) ? null : psi.WorkingDirectory;
            log.Info($"AppContainerLauncher: [5/5] Calling CreateProcessWithTokenW: cmd='{cmdLine}' workDir='{workDir}'");

            ProcessLaunchNative.PROCESS_INFORMATION pi;
            try
            {
                pi = ProcessLaunchNative.LaunchWithToken(hAppContainerToken, psi, envBlock.Pointer, @"WinSta0\Default");
            }
            catch (Win32Exception ex)
            {
                log.Error($"AppContainerLauncher: CreateProcessWithTokenW failed — Win32 error {ex.NativeErrorCode} (0x{ex.NativeErrorCode:X8}): {ex.Message}");
                throw;
            }

            NativeMethods.CloseHandle(pi.hThread);
            try
            {
                if (ProcessLaunchNative.WaitForSingleObject(pi.hProcess, 100) == ProcessLaunchNative.WAIT_OBJECT_0 &&
                    GetExitCodeProcess(pi.hProcess, out var exitCode))
                {
                    var hint = exitCode switch
                    {
                        0xC0000135 => " (DLL not found — the application may depend on DLLs not accessible to the container)",
                        0xC0000142 => " (DLL initialization failed — often user32.dll; may indicate missing desktop/winstation access)",
                        0xC0000022 => " (Access denied — the application may need additional file or registry permissions)",
                        _ => ""
                    };
                    log.Error($"AppContainerLauncher: Process exited immediately with code 0x{exitCode:X8}{hint}");
                    throw new InvalidOperationException(
                        $"Process exited immediately (code 0x{exitCode:X8}).{hint}");
                }

                log.Info($"AppContainerLauncher: Launched '{psi.FileName}' in container '{entry.Name}'");
            }
            finally
            {
                NativeMethods.CloseHandle(pi.hProcess);
            }
        }
        finally
        {
            foreach (var ptr in capSidPtrs)
                NativeMethods.LocalFree(ptr);
            if (pCapSids != IntPtr.Zero)
                Marshal.FreeHGlobal(pCapSids);
            if (pContainerSid != IntPtr.Zero)
                NativeMethods.LocalFree(pContainerSid);
            envBlock.Dispose();
            if (hAppContainerToken != IntPtr.Zero)
                NativeMethods.CloseHandle(hAppContainerToken);
            if (hDupToken != IntPtr.Zero)
                NativeMethods.CloseHandle(hDupToken);
            if (hExplorerToken != IntPtr.Zero)
                NativeMethods.CloseHandle(hExplorerToken);
        }
    }

    /// <summary>
    /// Creates an environment block for the AppContainer process.
    /// Uses the interactive user's token for <c>CreateEnvironmentBlock</c>, then overrides
    /// profile-related paths to match the container's data directory.
    /// Also grants the container SID access to VirtualStore and creates a VirtualStore shortcut.
    /// </summary>
    private static NativeEnvironmentBlock PrepareEnvironmentBlock(
        IntPtr hExplorerToken, AppContainerEntry entry, IntPtr pContainerSid,
        string exePath, ILoggingService log, IAppContainerEnvironmentSetup environmentSetup)
    {
        // Use the explorer token (not the AppContainer token) for CreateEnvironmentBlock —
        // AppContainer tokens may not have access to read profile environment variables.
        ProcessLaunchNative.CreateEnvironmentBlock(out var pEnvironment, hExplorerToken, false);

        if (pEnvironment == IntPtr.Zero)
            return new NativeEnvironmentBlock();

        var baseVars = NativeEnvironmentBlock.Read(pEnvironment);
        var localAppData = baseVars.GetValueOrDefault("LOCALAPPDATA");

        // OverrideProfileEnvironment always frees the original OS-allocated block (both on success
        // and failure) and returns an AllocHGlobal pointer on success or IntPtr.Zero on failure.
        var overriddenEnv = environmentSetup.OverrideProfileEnvironment(pEnvironment, entry.Name);
        NativeEnvironmentBlock envBlock;
        if (overriddenEnv != IntPtr.Zero)
        {
            envBlock = new NativeEnvironmentBlock(overriddenEnv, isOverridden: true);
        }
        else
        {
            // OverrideProfileEnvironment already freed the original block on failure.
            // Recreate a fresh OS-allocated block so the caller can still pass a valid environment.
            log.Warn($"AppContainerLauncher: OverrideProfileEnvironment failed for '{entry.Name}' — launching with unmodified environment as fallback");
            ProcessLaunchNative.CreateEnvironmentBlock(out pEnvironment, hExplorerToken, false);
            envBlock = new NativeEnvironmentBlock(pEnvironment, isOverridden: false);
        }

        if (localAppData != null)
        {
            // Grant container SID Modify on VirtualStore for UAC write virtualization
            environmentSetup.TryGrantVirtualStoreAccess(pContainerSid, localAppData);

            // Create a shortcut in the container data folder pointing to the exe's VirtualStore directory
            environmentSetup.TryCreateVirtualStoreShortcut(exePath, entry.Name, localAppData);
        }

        return envBlock;
    }
}