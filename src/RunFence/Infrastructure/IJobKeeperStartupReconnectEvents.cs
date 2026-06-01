namespace RunFence.Infrastructure;

public interface IJobKeeperStartupReconnectEvents
{
    event EventHandler<JobKeeperStartupReconnectCompletedEventArgs>? StartupReconnectCompleted;
}
