using RunFence.Core;
using RunFence.Firewall.Wfp;
using RunFence.Infrastructure;
using System.Threading;
using Timer = System.Threading.Timer;

namespace RunFence.Firewall;

/// <summary>
/// Background service that periodically computes cross-user ephemeral ports for blocked accounts
/// and updates the WFP blocker. Native endpoint ownership collection is delegated to
/// <see cref="IEphemeralPortOwnershipSnapshotProvider"/>. Runs on a 1-second timer.
/// </summary>
public class WfpEphemeralPortScanner(
    IWfpLocalhostBlocker wfpBlocker,
    UiThreadDatabaseAccessor db,
    IEphemeralPortOwnershipSnapshotProvider portOwnershipSnapshotProvider,
    ILoggingService log,
    bool startTimer)
    : IBackgroundService, IDisposable
{
    private record BlockedAccount(string Sid, List<PortRange> ExemptedRanges, bool FilterEphemeral);

    private Timer? _timer;
    private volatile bool _disposed;
    private int _scanInProgress;

    public void Start()
    {
        if (!startTimer)
        {
            OnTimerTick(null);
            return;
        }

        var timer = new Timer(OnTimerTick, null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        _timer = timer;
        if (_disposed)
        {
            _timer = null;
            timer.Dispose();
        }
    }

    private void OnTimerTick(object? state)
    {
        if (_disposed || Interlocked.Exchange(ref _scanInProgress, 1) == 1)
            return;

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            ScanAndApply();
            if (sw.ElapsedMilliseconds > 100)
                log.Warn($"WfpEphemeralPortScanner: scan took {sw.ElapsedMilliseconds} ms");
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            log.Warn($"WfpEphemeralPortScanner: scan failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _scanInProgress, 0);
            if (!_disposed)
            {
                try
                {
                    _timer?.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }
    }

    private void ScanAndApply()
    {
        var snapshot = db.CreateSnapshot();

        var blockedAccounts = snapshot.Accounts
            .Where(a => a.Firewall is { IsDefault: false, AllowLocalhost: false })
            .Select(a => new BlockedAccount(
                a.Sid,
                a.Firewall.LocalhostPortExemptions
                    .Select(LocalhostPortParser.ParsePortOrRange)
                    .Where(r => r.HasValue)
                    .Select(r => r!.Value)
                    .ToList(),
                a.Firewall.FilterEphemeralLoopback))
            .ToList();

        if (blockedAccounts.Count == 0)
            return;

        var eligibleAccounts = blockedAccounts.Where(a => a.FilterEphemeral).ToList();
        var clearAccounts = blockedAccounts.Where(a => !a.FilterEphemeral).ToList();

        Dictionary<int, PortOwnerSet> portOwners = [];
        if (eligibleAccounts.Count > 0)
            portOwners = portOwnershipSnapshotProvider.CollectListeningEphemeralPorts();

        foreach (var account in eligibleAccounts)
        {
            var ranges = ComputeBlockedEphemeralRanges(account.Sid, account.ExemptedRanges, portOwners);
            wfpBlocker.UpdateEphemeralPorts(account.Sid, ranges);
        }

        foreach (var account in clearAccounts)
            wfpBlocker.UpdateEphemeralPorts(account.Sid, []);
    }

    public static List<PortRange> ComputeBlockedEphemeralRanges(
        string accountSid,
        IReadOnlyList<PortRange> exemptedRanges,
        Dictionary<int, PortOwnerSet> portOwners)
    {
        var blocked = new List<int>();

        foreach (var (port, ownerSet) in portOwners)
        {
            var blockedByOwner = ownerSet.HasUnknownOwner
                                 || ownerSet.OwnerSids.Count != 1
                                 || !ownerSet.OwnerSids.Contains(accountSid);
            if (!blockedByOwner)
                continue;

            if (!exemptedRanges.Any(r => r.Low < r.High && r.Low <= port && port <= r.High))
                continue;

            if (exemptedRanges.Any(r => r.Low == r.High && r.Low == port))
                continue;

            blocked.Add(port);
        }

        return LocalhostPortParser.CoalescePortRanges(blocked);
    }

    public void Dispose()
    {
        _disposed = true;
        _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _timer?.Dispose();
        _timer = null;
    }
}
