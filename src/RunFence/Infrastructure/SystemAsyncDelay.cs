namespace RunFence.Infrastructure;

public sealed class SystemAsyncDelay : IAsyncDelay
{
    public Task Delay(TimeSpan delay) => Task.Delay(delay);
}
