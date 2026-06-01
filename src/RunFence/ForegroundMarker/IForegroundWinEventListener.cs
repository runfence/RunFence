namespace RunFence.ForegroundMarker;

public interface IForegroundWinEventListener : IDisposable
{
    event Action<IntPtr>? ForegroundChanged;
    event Action<IntPtr>? MoveSizeStarted;
    event Action<IntPtr>? MoveSizeEnded;
    event Action<IntPtr>? LocationChanged;

    void Start();
    void Stop();
}
