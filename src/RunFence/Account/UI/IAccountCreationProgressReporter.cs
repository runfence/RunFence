namespace RunFence.Account.UI;

public interface IAccountCreationProgressReporter
{
    CancellationToken CancellationToken { get; }

    void SetStatus(string message);
}
