namespace RunFence.Account.UI;

public interface IAccountCreationProgressRunner
{
    Task RunAsync(Func<IAccountCreationProgressReporter, Task> operation);
}
