namespace RunFence.Infrastructure;

/// <summary>
/// Thread-safe modal dialog depth tracker. Panels call <see cref="BeginModal"/> when
/// opening a dialog and <see cref="EndModal"/> when it closes.
/// </summary>
public class ModalTracker : IModalTracker
{
    private int _modalDepth;

    public void BeginModal() => Interlocked.Increment(ref _modalDepth);

    public void EndModal() => Interlocked.Decrement(ref _modalDepth);

    public bool AnyModalOpen => _modalDepth > 0;
}