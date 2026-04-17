using RunFence.Infrastructure;
using Timer = System.Threading.Timer;

namespace RunFence.Account.Lifecycle;

/// <summary>
/// Encapsulates the 1-hour recurring timer pattern shared by ephemeral cleanup services.
/// Dispatches ticks to the UI thread via <see cref="IUiThreadInvoker"/>.
/// </summary>
public sealed class EphemeralTimerHelper(IUiThreadInvoker uiThreadInvoker, Action onTick) : IDisposable
{
    private Timer? _timer;

    public void Start()
    {
        _timer = new Timer(
            OnTimerTick, null,
            TimeSpan.FromHours(1), TimeSpan.FromHours(1));
    }

    private void OnTimerTick(object? state)
    {
        try
        {
            uiThreadInvoker.Invoke(onTick);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}