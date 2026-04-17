using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Tokens;

namespace RunFence.Launch.Container;

public class AppContainerProcessLauncher(
    ILoggingService log,
    IAppContainerEnvironmentSetup environmentSetup,
    AppContainerProfileSetup appContainerProfileSetup,
    AppContainerDataFolderService dataFolderService,
    IExplorerTokenProvider explorerTokenProvider,
    IAppContainerSidProvider sidProvider)
    : IAppContainerProcessLauncher
{
    public ProcessInfo LaunchFile(ProcessLaunchTarget target, AppContainerLaunchIdentity identity)
    {
        var psi = target;
        var entry = identity.Entry;
        var containerSid = identity.Sid;

        dataFolderService.EnsureContainerDataFolder(entry, containerSid);
        dataFolderService.EnsureDataFolderTraverse(entry, containerSid);
        dataFolderService.EnsureInteractiveUserAccess(entry);

        log.Info($"AppContainerProcessLauncher: Starting launch of '{psi.ExePath}' in container '{entry.Name}'");

        IntPtr hExplorerToken = IntPtr.Zero;
        IntPtr hDupToken = IntPtr.Zero;
        IntPtr hAppContainerToken = IntPtr.Zero;
        IntPtr pContainerSid = IntPtr.Zero;
        IntPtr pCapSids = IntPtr.Zero;
        NativeEnvironmentBlock? envBlock = null;
        var capSidPtrs = new List<IntPtr>();

        try
        {
            hExplorerToken = explorerTokenProvider.GetSessionExplorerToken();
            log.Info("AppContainerProcessLauncher: [1/6] Acquired interactive user token");

            appContainerProfileSetup.EnsureProfileUnderToken(entry, hExplorerToken);

            var containerSidStr = sidProvider.GetSidString(entry.Name);
            if (!ProcessNative.ConvertStringSidToSid(containerSidStr, out pContainerSid))
            {
                var err = Marshal.GetLastWin32Error();
                throw new System.ComponentModel.Win32Exception(err, $"ConvertStringSidToSid failed for container SID '{containerSidStr}'");
            }

            log.Info($"AppContainerProcessLauncher: [2/6] Derived AppContainer SID: {containerSidStr}");

            var (capCount, capSidsPtr) = MarshalCapabilitySids(entry.Capabilities, capSidPtrs);
            pCapSids = capSidsPtr;

            log.Info($"AppContainerProcessLauncher: Capability count: {capCount}");

            hDupToken = NativeTokenAcquisition.DuplicateToken(hExplorerToken);

            var secCaps = new AppContainerProcessLauncherNative.SECURITY_CAPABILITIES
            {
                AppContainerSid = pContainerSid,
                Capabilities = pCapSids,
                CapabilityCount = capCount,
                Reserved = 0
            };
            if (!AppContainerProcessLauncherNative.CreateAppContainerToken(hDupToken, ref secCaps, out hAppContainerToken))
            {
                var err = Marshal.GetLastWin32Error();
                log.Error($"AppContainerProcessLauncher: CreateAppContainerToken failed — Win32 error {err} (0x{err:X8}): {new Win32Exception(err).Message}");
                throw new Win32Exception(err, "CreateAppContainerToken failed");
            }

            log.Info("AppContainerProcessLauncher: [3/6] Created AppContainer token via CreateAppContainerToken");

            var interactiveUserSid = SidResolutionHelper.GetInteractiveUserSid()
                ?? throw new InvalidOperationException("Interactive user SID is unavailable (explorer not running)");
            NativeTokenAcquisition.SetRestrictiveDefaultDacl(hAppContainerToken, containerSidStr, interactiveUserSid);

            log.Info("AppContainerProcessLauncher: [4/6] Set DACL");

            appContainerProfileSetup.TryEnableVirtualization(hAppContainerToken);

            try
            {
                var pathBuf = new StringBuilder(512);
                if (AppContainerProcessLauncherNative.GetAppContainerNamedObjectPath(hAppContainerToken, IntPtr.Zero, (uint)pathBuf.Capacity, pathBuf, out _))
                    log.Info($"AppContainerProcessLauncher: Named object path: {pathBuf}");
                else
                    log.Warn($"AppContainerProcessLauncher: GetAppContainerNamedObjectPath failed (error {Marshal.GetLastWin32Error()}) — named objects may not work");
            }
            catch (Exception ex)
            {
                log.Warn($"AppContainerProcessLauncher: Named object path check failed: {ex.Message}");
            }

            envBlock = PrepareEnvironmentBlock(hExplorerToken, entry, containerSidStr, psi.ExePath);

            envBlock.MergeInPlace(psi.EnvironmentVariables);

            log.Info("AppContainerProcessLauncher: [5/6] Built environment block with profile overrides");

            if (string.IsNullOrEmpty(psi.WorkingDirectory))
                psi = psi with { WorkingDirectory = Path.GetDirectoryName(psi.ExePath) };
            log.Info($"AppContainerProcessLauncher: [6/6] Calling CreateProcessWithTokenW");

            ProcessLaunchNative.PROCESS_INFORMATION pi;
            try
            {
                pi = ProcessLaunchNative.CreateProcessWithToken(hAppContainerToken, psi, envBlock.Pointer, log);
            }
            catch (Win32Exception ex)
            {
                log.Error($"AppContainerProcessLauncher: CreateProcessWithTokenW failed — Win32 error {ex.NativeErrorCode} (0x{ex.NativeErrorCode:X8}): {ex.Message}");
                throw;
            }
            
            CheckImmediateExit(pi);

            log.Info($"AppContainerProcessLauncher: Launched '{psi.ExePath}' in container '{entry.Name}'");

            return new ProcessInfo(pi);
        }
        finally
        {
            foreach (var ptr in capSidPtrs)
                ProcessNative.LocalFree(ptr);
            if (pCapSids != IntPtr.Zero)
                Marshal.FreeHGlobal(pCapSids);
            if (pContainerSid != IntPtr.Zero)
                ProcessNative.LocalFree(pContainerSid);
            envBlock?.Dispose();
            if (hAppContainerToken != IntPtr.Zero)
                ProcessNative.CloseHandle(hAppContainerToken);
            if (hDupToken != IntPtr.Zero)
                ProcessNative.CloseHandle(hDupToken);
            if (hExplorerToken != IntPtr.Zero)
                ProcessNative.CloseHandle(hExplorerToken);
        }
    }

    /// <summary>
    /// Converts capability SID strings to a native SID_AND_ATTRIBUTES array.
    /// Populates <paramref name="capSidPtrs"/> with successfully converted SID pointers
    /// (caller is responsible for freeing them via <c>LocalFree</c>).
    /// Returns the capability count and pointer to the allocated SID_AND_ATTRIBUTES array
    /// (caller is responsible for freeing via <c>Marshal.FreeHGlobal</c>); both are zero when no capabilities.
    /// </summary>
    private (uint CapCount, IntPtr PCapSids) MarshalCapabilitySids(
        IReadOnlyList<string>? capabilities, List<IntPtr> capSidPtrs)
    {
        if (capabilities is not { Count: > 0 })
            return (0u, IntPtr.Zero);

        foreach (var capSidStr in capabilities)
        {
            if (!ProcessNative.ConvertStringSidToSid(capSidStr, out var capSid))
            {
                log.Warn($"AppContainerProcessLauncher: Could not convert capability SID '{capSidStr}', skipping");
                continue;
            }

            capSidPtrs.Add(capSid);
        }

        if (capSidPtrs.Count == 0)
            return (0u, IntPtr.Zero);

        var capCount = (uint)capSidPtrs.Count;
        var capArraySize = Marshal.SizeOf<ProcessLaunchNative.SID_AND_ATTRIBUTES>() * capSidPtrs.Count;
        var pCapSids = Marshal.AllocHGlobal(capArraySize);
        for (int i = 0; i < capSidPtrs.Count; i++)
        {
            var item = new ProcessLaunchNative.SID_AND_ATTRIBUTES
            {
                Sid = capSidPtrs[i],
                Attributes = AppContainerProcessLauncherNative.SE_GROUP_ENABLED
            };
            Marshal.StructureToPtr(item, IntPtr.Add(pCapSids,
                i * Marshal.SizeOf<ProcessLaunchNative.SID_AND_ATTRIBUTES>()), false);
        }

        return (capCount, pCapSids);
    }

    /// <summary>
    /// Checks whether the process exited immediately (within 100ms) with a non-zero exit code.
    /// Throws <see cref="InvalidOperationException"/> with a diagnostic hint when it did.
    /// </summary>
    private void CheckImmediateExit(ProcessLaunchNative.PROCESS_INFORMATION pi)
    {
        if (ProcessLaunchNative.WaitForSingleObject(pi.hProcess, 100) == ProcessLaunchNative.WAIT_OBJECT_0 &&
            ProcessLaunchNative.GetExitCodeProcess(pi.hProcess, out var exitCode) &&
            exitCode != 0)
        {
            var hint = exitCode switch
            {
                0xC0000135 => " (DLL not found — the application may depend on DLLs not accessible to the container)",
                0xC0000142 => " (DLL initialization failed — often user32.dll; may indicate missing desktop/winstation access)",
                0xC0000022 => " (Access denied — the application may need additional file or registry permissions)",
                _ => ""
            };
            log.Error($"AppContainerProcessLauncher: Process exited immediately with code 0x{exitCode:X8}{hint}");
            throw new InvalidOperationException(
                $"Process exited immediately (code 0x{exitCode:X8}).{hint}");
        }
    }

    /// <summary>
    /// Creates an environment block for the AppContainer process.
    /// Uses the interactive user's token for <c>CreateEnvironmentBlock</c>, then overrides
    /// profile-related paths to match the container's data directory.
    /// Also grants the container SID access to VirtualStore and creates a VirtualStore shortcut.
    /// </summary>
    private NativeEnvironmentBlock PrepareEnvironmentBlock(
        IntPtr hExplorerToken, AppContainerEntry entry, string containerSidStr, string exePath)
    {
        ProcessLaunchNative.CreateEnvironmentBlock(out var pEnvironment, hExplorerToken, false);

        if (pEnvironment == IntPtr.Zero)
            return new NativeEnvironmentBlock();

        Dictionary<string, string> baseVars;
        try
        {
            baseVars = NativeEnvironmentBlock.Read(pEnvironment);
        }
        catch
        {
            ProcessLaunchNative.DestroyEnvironmentBlock(pEnvironment);
            throw;
        }
        var localAppData = baseVars.GetValueOrDefault("LOCALAPPDATA");

        var overriddenEnv = environmentSetup.OverrideProfileEnvironment(pEnvironment, entry.Name);
        NativeEnvironmentBlock envBlock;
        if (overriddenEnv != IntPtr.Zero)
        {
            envBlock = new NativeEnvironmentBlock(overriddenEnv, isOverridden: true);
        }
        else
        {
            log.Warn($"AppContainerProcessLauncher: OverrideProfileEnvironment failed for '{entry.Name}' — launching with unmodified environment as fallback");
            ProcessLaunchNative.CreateEnvironmentBlock(out pEnvironment, hExplorerToken, false);
            envBlock = new NativeEnvironmentBlock(pEnvironment, isOverridden: false);
        }

        if (localAppData != null)
        {
            environmentSetup.TryGrantVirtualStoreAccess(containerSidStr, localAppData);
            environmentSetup.TryCreateVirtualStoreShortcut(exePath, entry.Name, localAppData);
        }

        return envBlock;
    }
}