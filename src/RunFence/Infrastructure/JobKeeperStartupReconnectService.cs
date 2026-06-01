using System.Security.Principal;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Infrastructure;

public sealed class JobKeeperStartupReconnectService(
    IJobKeeperIdentityStore identityStore,
    IJobKeeperService jobKeeperService,
    IVerifiedRestrictedJobCache verifiedRestrictedJobCache,
    ILoggingService log) : IBackgroundService, IJobKeeperStartupReconnectEvents, IDisposable
{
    private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromSeconds(60);
    private readonly CancellationTokenSource disposeCts = new();
    private readonly object gate = new();
    private Task? backgroundTask;
    private bool completionRaised;
    private bool disposed;

    public event EventHandler<JobKeeperStartupReconnectCompletedEventArgs>? StartupReconnectCompleted;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        lock (gate)
        {
            ThrowIfDisposed();
            backgroundTask ??= Task.Run(RunBackgroundAsync);
        }
    }

    public Task RunInitialReconnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        var reconnectedCount = 0;
        string? failureMessage = null;
        try
        {
            foreach (var identity in identityStore.GetAll())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryReconnect(identity))
                    continue;

                reconnectedCount++;
            }
        }
        catch (OperationCanceledException)
        {
            failureMessage = "Canceled";
            throw;
        }
        catch (Exception ex)
        {
            failureMessage = ex.Message;
            log.Warn($"JobKeeperStartupReconnectService: initial reconnect failed: {ex.Message}");
        }
        finally
        {
            RaiseCompletionOnce(reconnectedCount, failureMessage);
        }

        return Task.CompletedTask;
    }

    public void RunMaintenanceCycle()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        verifiedRestrictedJobCache.SweepEmptyOrInvalidJobs();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        Task? taskToDisposeAfter;
        lock (gate)
        {
            if (disposed)
                return;

            disposed = true;
            disposeCts.Cancel();
            taskToDisposeAfter = backgroundTask;
        }

        if (taskToDisposeAfter == null || taskToDisposeAfter.IsCompleted)
        {
            disposeCts.Dispose();
            return;
        }

        _ = taskToDisposeAfter.ContinueWith(
            _ => disposeCts.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private bool TryReconnect(JobKeeperInstanceIdentity identity)
    {
        var isLow = identity.ExpectedMode == JobKeeperIntegrityMode.LowIntegrity;
        try
        {
            var targetSid = new SecurityIdentifier(identity.TargetSid);
            var pid = jobKeeperService.TryReconnectExistingJobKeeper(identity.TargetSid, isLow, targetSid);
            return pid > 0;
        }
        catch (Exception ex)
        {
            log.Warn(
                $"JobKeeperStartupReconnectService: failed to reconnect keeper for {identity.TargetSid} ({identity.ExpectedMode}): {ex.Message}");
            return false;
        }
    }

    private async Task RunBackgroundAsync()
    {
        try
        {
            await RunInitialReconnectAsync(disposeCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            RaiseCompletionOnce(0, "Canceled");
            return;
        }

        while (!disposeCts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(MaintenanceInterval, disposeCts.Token).ConfigureAwait(false);
                if (disposeCts.IsCancellationRequested)
                    break;

                RunMaintenanceCycle();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                log.Warn($"JobKeeperStartupReconnectService: maintenance sweep failed: {ex.Message}");
            }
        }
    }

    private void RaiseCompletionOnce(int reconnectedCount, string? failureMessage)
    {
        EventHandler<JobKeeperStartupReconnectCompletedEventArgs>? handler = null;
        lock (gate)
        {
            if (completionRaised)
                return;

            completionRaised = true;
            handler = StartupReconnectCompleted;
        }

        handler?.Invoke(
            this,
            new JobKeeperStartupReconnectCompletedEventArgs(reconnectedCount, failureMessage));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
