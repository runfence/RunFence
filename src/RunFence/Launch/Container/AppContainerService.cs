using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using RunFence.Acl.Traverse;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Tokens;

namespace RunFence.Launch.Container;

public class AppContainerService(ILoggingService log, IUserTraverseService userTraverseService, IAppContainerEnvironmentSetup environmentSetup, AppContainerProfileSetup appContainerProfileSetup)
    : IAppContainerService
{
    private readonly AppContainerComAccessService _comService = new(log);
    private readonly AppContainerDataFolderService _dataFolderService = new(log, userTraverseService);

    public void CreateProfile(AppContainerEntry entry)
    {
        var hToken = IntPtr.Zero;
        try
        {
            hToken = ExplorerTokenHelper.GetSessionExplorerToken(log);
            appContainerProfileSetup.EnsureProfileUnderToken(entry, hToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            log.Warn($"AppContainerService: GetSessionExplorerToken failed for '{entry.Name}': {ex.Message} — falling back to linked token (no shell folder redirects)");
            LinkedTokenHelper.RunUnderLinkedToken(_ =>
            {
                var hr = AppContainerNative.CreateAppContainerProfile(
                    entry.Name, entry.DisplayName,
                    $"RunFence AppContainer: {entry.DisplayName}",
                    IntPtr.Zero, 0, out var sid);

                if (sid != IntPtr.Zero)
                    NativeMethods.LocalFree(sid);

                if (hr != 0 && hr != ProcessLaunchNative.HrAlreadyExists)
                    throw new InvalidOperationException(
                        $"CreateAppContainerProfile failed with HRESULT 0x{hr:X8} for '{entry.Name}'");
            }, log);
        }
        finally
        {
            if (hToken != IntPtr.Zero)
                NativeMethods.CloseHandle(hToken);
        }

        _dataFolderService.EnsureContainerDataFolder(entry, GetSid(entry.Name));
        _dataFolderService.EnsureInteractiveUserAccess(entry);
    }

    public void EnsureProfile(AppContainerEntry entry)
    {
        // DeriveAppContainerSidFromAppContainerName cannot reliably detect whether
        // the OS profile actually exists (it's a pure computation).
        CreateProfile(entry);
    }

    public bool ProfileExists(string name)
    {
        // Check the AppContainer Mappings registry key (under the current user's hive).
        // This works reliably when the elevated user is the same as the interactive user.
        // For cross-user setups, EnsureProfile's idempotent CreateProfile call is the safe fallback.
        try
        {
            var hr = AppContainerNative.DeriveAppContainerSidFromAppContainerName(name, out var pSid);
            if (hr != 0 || pSid == IntPtr.Zero)
                return false;
            string? sidStr = null;
            try
            {
                if (AppContainerNative.ConvertSidToStringSid(pSid, out var pStr))
                {
                    try
                    {
                        sidStr = Marshal.PtrToStringUni(pStr);
                    }
                    finally
                    {
                        NativeMethods.LocalFree(pStr);
                    }
                }
            }
            finally
            {
                NativeMethods.LocalFree(pSid);
            }

            if (sidStr == null)
                return false;
            const string mappingsPath =
                @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings";
            using var key = Registry.CurrentUser.OpenSubKey($@"{mappingsPath}\{sidStr}");
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    public void DeleteProfile(string name, bool hadLoopback = false)
    {
        if (hadLoopback)
            SetLoopbackExemption(name, false);

        var hr = AppContainerNative.DeleteAppContainerProfile(name);
        if (hr != 0)
            log.Warn($"DeleteAppContainerProfile returned HRESULT 0x{hr:X8} for '{name}'");

        var dataPath = AppContainerPaths.GetContainerDataPath(name);
        if (Directory.Exists(dataPath))
        {
            try
            {
                Directory.Delete(dataPath, true);
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to delete container data folder '{dataPath}': {ex.Message}");
            }
        }
    }

    public string GetSid(string name)
    {
        var hr = AppContainerNative.DeriveAppContainerSidFromAppContainerName(name, out var pSid);
        if (hr != 0 || pSid == IntPtr.Zero)
            throw new InvalidOperationException(
                $"DeriveAppContainerSidFromAppContainerName failed with HRESULT 0x{hr:X8} for '{name}'");

        try
        {
            if (!AppContainerNative.ConvertSidToStringSid(pSid, out var strPtr))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                return Marshal.PtrToStringUni(strPtr) ?? throw new InvalidOperationException("Null SID string");
            }
            finally
            {
                NativeMethods.LocalFree(strPtr);
            }
        }
        finally
        {
            NativeMethods.LocalFree(pSid);
        }
    }

    public string GetContainerDataPath(string name)
        => AppContainerPaths.GetContainerDataPath(name);

    public void Launch(AppEntry app, AppContainerEntry entry, string? launcherArguments, string? launcherWorkingDirectory = null)
    {
        var containerSid = GetSid(entry.Name);
        _dataFolderService.EnsureContainerDataFolder(entry, containerSid); // idempotent: creates dirs + ACLs if missing

        // Ensure the container can traverse to its data folder (Roaming etc.)
        // CreateProfile sets this up initially, but re-apply on every launch in case
        // ACLs were lost (e.g., after RevertTraverseAccess, Windows Update, etc.)
        _dataFolderService.EnsureDataFolderTraverse(entry, containerSid);

        // AppContainer dual access check: the interactive user's token must also have
        // access to the data folder (step 1 of the dual check). When elevated ≠ interactive,
        // the container SID ACEs alone (step 2) aren't sufficient.
        _dataFolderService.EnsureInteractiveUserAccess(entry);

        var workingDirectory = ProcessLaunchHelper.DetermineWorkingDirectory(app, launcherWorkingDirectory)
                               ?? Path.GetDirectoryName(Path.GetFullPath(app.ExePath))
                               ?? "";

        var psi = new ProcessStartInfo
        {
            FileName = app.ExePath,
            WorkingDirectory = workingDirectory
        };

        var args = ProcessLaunchHelper.DetermineArguments(app, launcherArguments);
        if (!string.IsNullOrEmpty(args))
            psi.Arguments = args;

        AppContainerLauncher.Launch(psi, entry, log, environmentSetup, appContainerProfileSetup, app.EnvironmentVariables);
    }

    public bool SetLoopbackExemption(string name, bool enable)
    {
        var arg = enable ? "-a" : "-d";
        try
        {
            var checkNetIsolation = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "CheckNetIsolation.exe");

            if (!File.Exists(checkNetIsolation))
            {
                log.Warn($"CheckNetIsolation.exe not found at '{checkNetIsolation}' — loopback exemption not changed");
                return false;
            }

            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = checkNetIsolation,
                Arguments = $"LoopbackExempt {arg} -n={name}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (proc != null && !proc.WaitForExit(5000))
            {
                log.Warn($"CheckNetIsolation LoopbackExempt {arg} for '{name}' timed out after 5 seconds");
                try
                {
                    proc.Kill();
                }
                catch
                {
                }

                return false;
            }

            var exitCode = proc?.ExitCode ?? -1;
            if (exitCode != 0)
            {
                log.Warn($"CheckNetIsolation LoopbackExempt {arg} for '{name}' exited with code {exitCode}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            log.Warn($"SetLoopbackExemption({enable}) failed for '{name}': {ex.Message}");
            return false;
        }
    }

    public void GrantComAccess(string containerSid, string clsid)
        => _comService.GrantComAccess(containerSid, clsid);

    public void RevokeComAccess(string containerSid, string clsid)
        => _comService.RevokeComAccess(containerSid, clsid);

    public (bool Modified, List<string> AppliedPaths) EnsureTraverseAccess(AppContainerEntry entry, string path)
    {
        var containerSid = GetSid(entry.Name);
        return userTraverseService.EnsureTraverseAccess(containerSid, path);
    }

    public void RevertTraverseAccess(AppContainerEntry entry, AppDatabase database)
    {
        var containerSid = GetSid(entry.Name);
        userTraverseService.RevertTraverseAccess(containerSid, database);
    }

    public void RevertTraverseAccessForPath(AppContainerEntry entry, GrantedPathEntry grantedEntry, AppDatabase database)
    {
        var containerSid = GetSid(entry.Name);
        userTraverseService.RevertTraverseAccessForPath(containerSid, grantedEntry, database);
    }
}