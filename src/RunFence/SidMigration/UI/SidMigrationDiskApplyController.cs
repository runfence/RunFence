using RunFence.Infrastructure;

namespace RunFence.SidMigration.UI;

public sealed class SidMigrationDiskApplyController
{
    private readonly IClock _clock;
    private DateTime? _startUtc;

    public SidMigrationDiskApplyController(IClock clock)
    {
        _clock = clock;
    }

    public void Start()
    {
        _startUtc = _clock.UtcNow;
    }

    public bool TryRequestCancellation(CancellationTokenSource cts, Func<bool> confirmCancel)
    {
        if (_startUtc == null)
            return false;

        if (_clock.UtcNow - _startUtc < TimeSpan.FromSeconds(10))
            return false;

        if (!confirmCancel())
            return false;

        cts.Cancel();
        return true;
    }
}

