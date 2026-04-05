namespace RunFence.Infrastructure;

public interface IPreviousWindowTracker
{
    IntPtr PreviousWindow { get; }
}