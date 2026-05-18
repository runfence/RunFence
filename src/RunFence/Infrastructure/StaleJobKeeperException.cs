namespace RunFence.Infrastructure;

public sealed class StaleJobKeeperException(string sid)
    : InvalidOperationException($"Job keeper is no longer available for {sid}.")
{
    public string Sid { get; } = sid;
}
