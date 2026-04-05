using RunFence.Infrastructure;
using Timer = System.Threading.Timer;

namespace RunFence.Account.Lifecycle;

/// <summary>
/// Encapsulates the 1-hour recurring timer pattern shared by ephemeral cleanup services.
/// Dispatches ticks to the UI thread via <see cref="IUiThreadInvoker"/>.
/// </summary>
public sealed class EphemeralTimerHelper : IDisposable
{
    private Timer? _timer;
    private readonly IUiThreadInvoker _uiThreadInvoker;
    private readonly Action _onTick;

    public EphemeralTimerHelper(IUiThreadInvoker uiThreadInvoker, Action onTick)
    {
        _uiThreadInvoker = uiThreadInvoker;
        _onTick = onTick;
    }

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
            _uiThreadInvoker.Invoke(_onTick);
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