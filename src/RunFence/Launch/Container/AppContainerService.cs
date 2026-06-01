using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Tokens;
using RunFence.Persistence;

namespace RunFence.Launch.Container;

public class AppContainerService(
    ILoggingService log,
    ITraverseService traverseService,
    IGrantAccountCleanupService grantAccountCleanupService,
    IAppContainerProfileSetup appContainerProfileSetup,
    IAppContainerDataFolderService dataFolderService,
    IAppContainerComAccessService comService,
    IExplorerTokenProvider explorerTokenProvider,
    IAppContainerSidProvider sidProvider,
    IAppContainerUserRegistryRoot userRegistryRoot,
    IAppContainerPathProvider pathProvider,
    ITrackingJobStateStore? trackingJobStateStore = null)
    : IAppContainerService
{
    private readonly IRegistryKey _usersRoot = userRegistryRoot.UsersRoot;
    private readonly IAppContainerPathProvider _pathProvider = pathProvider;

    public AppContainerProfileSetupResult CreateProfile(AppContainerEntry entry)
    {
        var hToken = IntPtr.Zero;
        try
        {
            hToken = explorerTokenProvider.GetExplorerToken();
            var profileResult = appContainerProfileSetup.EnsureProfileUnderToken(entry, hToken);
            if (profileResult.Status != AppContainerProfileSetupStatus.Succeeded)
                return profileResult;

            var containerSid = GetSid(entry.Name);
            dataFolderService.EnsureContainerDataFolder(entry, containerSid);
            dataFolderService.EnsureInteractiveUserAccess(entry);
            return profileResult;
        }
        finally
        {
            if (hToken != IntPtr.Zero)
                ProcessNative.CloseHandle(hToken);
        }
    }

    public AppContainerProfileSetupResult EnsureProfile(AppContainerEntry entry)
    {
        if (ProfileExists(entry.Name))
            return AppContainerProfileSetupResult.Success(profileCreatedOrAlreadyExists: true);

        return CreateProfile(entry);
    }

    public bool ProfileExists(string name)
    {
        try
        {
            var interactiveUserSid = TryGetVerifiedExplorerSid();
            if (string.IsNullOrWhiteSpace(interactiveUserSid))
                return false;

            var sidStr = sidProvider.GetSidString(name);
            using var key = _usersRoot.OpenSubKey(
                $@"{interactiveUserSid}\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings\{sidStr}");
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    public async Task DeleteProfile(string name, bool hadLoopback = false)
    {
        if (hadLoopback)
        {
            var loopbackRemoved = await SetLoopbackExemption(name, false);
            if (!loopbackRemoved)
                throw new InvalidOperationException($"Failed to remove loopback exemption for '{name}'.");
        }

        var hr = DeleteAppContainerProfile(name);
        if (hr != 0)
            throw new InvalidOperationException(
                $"DeleteAppContainerProfile returned HRESULT 0x{hr:X8} for '{name}'.");

        var containerSid = GetSid(name);
        trackingJobStateStore?.RemoveTrackingJobSid(containerSid, saveImmediately: false);
        RemoveContainerStateFromLoadedHives(containerSid);

        var dataPath = _pathProvider.GetContainerDataPath(name);
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

    protected virtual int DeleteAppContainerProfile(string name)
        => AppContainerNative.DeleteAppContainerProfile(name);

    public string GetSid(string name) => sidProvider.GetSidString(name);

    public string GetContainerDataPath(string name)
        => _pathProvider.GetContainerDataPath(name);

    public async Task<bool> SetLoopbackExemption(string name, bool enable)
    {
        var arg = enable ? "-a" : "-d";
        try
        {
            var checkNetIsolation = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "CheckNetIsolation.exe");

            if (!File.Exists(checkNetIsolation))
            {
                log.Warn($"CheckNetIsolation.exe not found at '{checkNetIsolation}' - loopback exemption not changed");
                return false;
            }

            return await Task.Run(() =>
            {
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
            });
        }
        catch (Exception ex)
        {
            log.Warn($"SetLoopbackExemption({enable}) failed for '{name}': {ex.Message}");
            return false;
        }
    }

    public AppContainerComAccessResult GrantComAccess(string containerSid, string clsid)
        => comService.GrantComAccess(containerSid, clsid);

    public AppContainerComAccessResult RevokeComAccess(string containerSid, string clsid)
        => comService.RevokeComAccess(containerSid, clsid);

    public (bool Modified, List<string> AppliedPaths) EnsureTraverseAccess(AppContainerEntry entry, string path)
    {
        var containerSid = GetSid(entry.Name);
        var result = traverseService.AddTraverse(containerSid, path);
        return (
            result.DatabaseModified || result.TraverseApplied,
            CollectTraverseCoveragePaths(path));
    }

    public GrantApplyResult RevertTraverseAccess(AppContainerEntry entry, AppDatabase database)
    {
        _ = database;
        var containerSid = !string.IsNullOrWhiteSpace(entry.Sid)
            ? entry.Sid
            : GetSid(entry.Name);
        return grantAccountCleanupService.RemoveAll(containerSid);
    }

    private static List<string> CollectTraverseCoveragePaths(string path)
    {
        var normalized = Path.GetFullPath(path);
        var paths = new List<string>();
        var current = new DirectoryInfo(normalized);
        while (current != null)
        {
            paths.Add(current.FullName);
            current = current.Parent;
        }

        return paths;
    }

    private string? TryGetVerifiedExplorerSid()
    {
        var hToken = IntPtr.Zero;
        try
        {
            hToken = explorerTokenProvider.TryGetExplorerToken();
            return hToken == IntPtr.Zero ? null : GetTokenSid(hToken);
        }
        finally
        {
            if (hToken != IntPtr.Zero)
                ProcessNative.CloseHandle(hToken);
        }
    }

    private static string? GetTokenSid(IntPtr hToken)
    {
        const int tokenUser = 1;
        ProcessNative.GetTokenInformation(hToken, tokenUser, IntPtr.Zero, 0, out var needed);
        if (needed <= 0)
            return null;

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!ProcessNative.GetTokenInformation(hToken, tokenUser, buffer, needed, out _))
                return null;

            return new SecurityIdentifier(Marshal.ReadIntPtr(buffer)).Value;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void RemoveContainerStateFromLoadedHives(string containerSid)
    {
        foreach (var hiveName in _usersRoot.GetSubKeyNames())
        {
            if (!LooksLikeLoadedUserHive(hiveName))
                continue;

            TryDeleteUserSubKeyTree(
                $@"{hiveName}\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings\{containerSid}");
            TryDeleteUserSubKeyTree(
                $@"{hiveName}\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Storage\{containerSid}");
        }
    }

    private void TryDeleteUserSubKeyTree(string subKeyPath)
    {
        try
        {
            _usersRoot.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to remove AppContainer registry state '{subKeyPath}': {ex.Message}");
        }
    }

    private static bool LooksLikeLoadedUserHive(string hiveName)
        => hiveName.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase)
           && !hiveName.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase);
}
