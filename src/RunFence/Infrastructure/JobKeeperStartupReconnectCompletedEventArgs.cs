namespace RunFence.Infrastructure;

public sealed class JobKeeperStartupReconnectCompletedEventArgs : EventArgs
{
    public JobKeeperStartupReconnectCompletedEventArgs(int reconnectedCount, string? failureMessage)
    {
        ReconnectedCount = reconnectedCount;
        FailureMessage = failureMessage;
    }

    public int ReconnectedCount { get; }
    public string? FailureMessage { get; }
    public bool Succeeded => string.IsNullOrWhiteSpace(FailureMessage);
}
