namespace RunFence.Acl.UI;

public sealed class AclManagerScanCancellationController
{
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _activeSource;

    public CancellationToken BeginScan()
    {
        lock (_syncRoot)
        {
            if (_activeSource != null)
                throw new InvalidOperationException("An ACL manager scan is already active.");

            _activeSource = new CancellationTokenSource();
            return _activeSource.Token;
        }
    }

    public void CancelActiveScan()
    {
        CancellationTokenSource? sourceToCancel;
        lock (_syncRoot)
        {
            sourceToCancel = _activeSource;
        }

        if (sourceToCancel == null)
            return;

        try
        {
            sourceToCancel.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void CompleteScan()
    {
        CancellationTokenSource? sourceToDispose;
        lock (_syncRoot)
        {
            sourceToDispose = _activeSource;
            _activeSource = null;
        }

        sourceToDispose?.Dispose();
    }
}
