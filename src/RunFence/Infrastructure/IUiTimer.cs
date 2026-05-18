namespace RunFence.Infrastructure;

public interface IUiTimer : IDisposable
{
    bool Enabled { get; }
    int Interval { get; set; }
    event EventHandler? Tick;
    void Start();
    void Stop();
}
