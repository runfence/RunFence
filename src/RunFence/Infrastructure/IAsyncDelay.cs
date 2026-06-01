namespace RunFence.Infrastructure;

public interface IAsyncDelay
{
    Task Delay(TimeSpan delay);
}
