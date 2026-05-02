namespace RunFence.Infrastructure;

/// <summary>
/// Abstraction over a restartable, stoppable UI-thread timer backed by
/// <see cref="System.Windows.Forms.Timer"/> in production.
/// Allows deterministic testing of timer-driven behavior without a WinForms message loop.
/// </summary>
public interface IUiTimer : IDisposable
{
    int Interval { get; set; }
    void Start();
    void Stop();
    event EventHandler Tick;
}
