namespace RunFence.Infrastructure;

public interface IClock
{
    DateTime UtcNow { get; }
}
