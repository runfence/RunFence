using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Tokens;

namespace RunFence.Launch.Container;

public class AppContainerService(ILoggingService log, IPathGrantService pathGrantService, AppContainerProfileSetup appContainerProfileSetup, AppContainerDataFolderService dataFolderService, Func<AppContainerComAccessService> comServiceFactory, IExplorerTokenProvider explorerTokenProvider, IAppContainerSidProvider sidProvider)
    : IAppContainerService
{

    public void CreateProfile(AppContainerEntry entry)
    {
        var hToken = IntPtr.Zero;
        try
        {
            hToken = explorerTokenProvider.GetSessionExplorerToken();
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
                    ProcessNative.LocalFree(sid);

                if (hr != 0 && hr != ProcessLaunchNative.HrAlreadyExists)
                    throw new InvalidOperationException(
                        $"CreateAppContainerProfile failed with HRESULT 0x{hr:X8} for '{entry.Name}'");
            }, log);
        }
        finally
        {
            if (hToken != IntPtr.Zero)
                ProcessNative.CloseHandle(hToken);
        }

        dataFolderService.EnsureContainerDataFolder(entry, GetSid(entry.Name));
        dataFolderService.EnsureInteractiveUserAccess(entry);
    }

    public void EnsureProfile(AppContainerEntry entry)
    {
        // Skip creation when the profile already exists to avoid the overhead of calling
        // CreateAppContainerProfile (and the explorer token acquisition it triggers) on every launch.
        if (ProfileExists(entry.Name))
            return;
        CreateProfile(entry);
    }

    public bool ProfileExists(string name)
    {
        // Check the AppContainer Mappings registry key (under the current user's hive).
        // This works reliably when the elevated user is the same as the interactive user.
        // For cross-user setups, EnsureProfile's idempotent CreateProfile call is the safe fallback.
        try
        {
            var sidStr = sidProvider.GetSidString(name);
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

    public string GetSid(string name) => sidProvider.GetSidString(name);

    public string GetContainerDataPath(string name)
        => AppContainerPaths.GetContainerDataPath(name);

    /// <remarks>
    /// <see cref="Process.WaitForExit(int)"/> with a 5000ms timeout blocks the calling thread for up to 5 seconds.
    /// Callers that run on the UI thread (<c>AppContainerEditService</c>, <c>AccountContainerOrchestrator</c>)
    /// will freeze the UI for up to 5 seconds. Callers should ideally dispatch to a background thread.
    /// </remarks>
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
        => comServiceFactory().GrantComAccess(containerSid, clsid);

    public void RevokeComAccess(string containerSid, string clsid)
        => comServiceFactory().RevokeComAccess(containerSid, clsid);

    public (bool Modified, List<string> AppliedPaths) EnsureTraverseAccess(AppContainerEntry entry, string path)
    {
        var containerSid = GetSid(entry.Name);
        return pathGrantService.AddTraverse(containerSid, path);
    }

    public void RevertTraverseAccess(AppContainerEntry entry, AppDatabase database)
    {
        var containerSid = GetSid(entry.Name);
        pathGrantService.RemoveAll(containerSid, updateFileSystem: true);
    }

    public void RevertTraverseAccessForPath(AppContainerEntry entry, GrantedPathEntry grantedEntry, AppDatabase database)
    {
        var containerSid = GetSid(entry.Name);
        pathGrantService.RemoveTraverse(containerSid, grantedEntry.Path, updateFileSystem: true);
    }
}