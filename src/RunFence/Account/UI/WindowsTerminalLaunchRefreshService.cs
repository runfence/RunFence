using System.Threading;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Persistence;

namespace RunFence.Account.UI;

public interface IWindowsTerminalLaunchRefreshService
{
    Task EnsureSharedDeploymentExistsBeforeTerminalLaunchAsync(LaunchIdentity identity);
    Task EnsureSharedDeploymentExistsBeforeTerminalLaunchAsync(string sid);
    void TryStartOnlineRefreshAfterTerminalLaunch(LaunchIdentity identity);
    void TryStartOnlineRefreshAfterTerminalLaunch(string sid);
}

public sealed class WindowsTerminalLaunchRefreshService(
    IAppLockControl appLockControl,
    IDatabaseProvider databaseProvider,
    ISessionSaver sessionSaver,
    IWindowsTerminalAccountStateService accountStateService,
    IWindowsTerminalDeploymentService deploymentService,
    IWindowsTerminalDeploymentProgressRunner progressRunner,
    WindowsTerminalDeploymentPaths deploymentPaths,
    ILoggingService log)
    : IWindowsTerminalLaunchRefreshService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromDays(7);
    private int _onlineRefreshInProgress;

    public async Task EnsureSharedDeploymentExistsBeforeTerminalLaunchAsync(LaunchIdentity identity)
    {
        if (identity is not AccountLaunchIdentity)
            return;

        try
        {
            if (appLockControl.IsLocked)
                return;

            if (File.Exists(deploymentPaths.SharedExecutablePath))
                return;

            await progressRunner.RunAsync(
                "Installing standalone Windows Terminal from official GitHub...",
                cancellationToken =>
                {
                    if (appLockControl.IsLocked)
                        return Task.CompletedTask;

                    return deploymentService.EnsureSharedDeploymentReadyAsync(cancellationToken);
                });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            log.Warn($"Windows Terminal deployment failed and terminal launch will continue with the system terminal: {ex.Message}");
        }
    }

    public Task EnsureSharedDeploymentExistsBeforeTerminalLaunchAsync(string sid)
        => EnsureSharedDeploymentExistsBeforeTerminalLaunchAsync(new AccountLaunchIdentity(sid));

    public void TryStartOnlineRefreshAfterTerminalLaunch(LaunchIdentity identity)
    {
        if (identity is AccountLaunchIdentity accountIdentity)
            TryStartOnlineRefreshAfterTerminalLaunch(accountIdentity);
    }

    public void TryStartOnlineRefreshAfterTerminalLaunch(string sid)
        => TryStartOnlineRefreshAfterTerminalLaunch(new AccountLaunchIdentity(sid));

    private void TryStartOnlineRefreshAfterTerminalLaunch(AccountLaunchIdentity identity)
    {
        try
        {
            if (appLockControl.IsLocked)
                return;

            if (!UsesSharedWindowsTerminal(identity))
                return;

            TryStartOnlineRefresh();
        }
        catch (Exception ex)
        {
            log.Warn($"Windows Terminal background launch refresh was not started after terminal launch: {ex.Message}");
        }
    }

    private void TryStartOnlineRefresh()
    {
        if (Interlocked.CompareExchange(ref _onlineRefreshInProgress, 1, 0) != 0)
            return;

        Task.Run(async () =>
        {
            try
            {
                if (appLockControl.IsLocked)
                    return;

                await deploymentService.TryDeployLatestCachedZipIfNewerThanSharedAsync(CancellationToken.None);

                if (appLockControl.IsLocked)
                    return;

                bool shouldRefreshOnline;
                try
                {
                    shouldRefreshOnline = TryReserveRefreshAttempt();
                }
                catch (Exception ex)
                {
                    log.Warn($"Windows Terminal refresh timestamp could not be saved and online cache refresh will be skipped: {ex.Message}");
                    return;
                }

                if (shouldRefreshOnline && !appLockControl.IsLocked)
                    await deploymentService.EnsureLatestReleaseCachedAsync(CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                log.Warn($"Windows Terminal background launch refresh failed and terminal launch already continued: {ex.Message}");
            }
            finally
            {
                Volatile.Write(ref _onlineRefreshInProgress, 0);
            }
        });
    }

    private bool UsesSharedWindowsTerminal(AccountLaunchIdentity identity)
        => deploymentPaths.IsSharedExecutablePath(accountStateService.ResolveLaunchTarget(identity));

    private bool TryReserveRefreshAttempt()
    {
        var settings = databaseProvider.GetDatabase().Settings;
        var now = DateTime.UtcNow;
        if (settings.LastWindowsTerminalLaunchRefreshAttemptUtc is { } lastAttempt &&
            now - lastAttempt < RefreshInterval)
        {
            return false;
        }

        settings.LastWindowsTerminalLaunchRefreshAttemptUtc = now;
        sessionSaver.SaveConfig();
        return true;
    }
}
