using RunFence.Account.UI;

namespace RunFence.Tests.Helpers;

internal sealed class ImmediateAccountCreationProgressRunner : IAccountCreationProgressRunner
{
    public Task RunAsync(Func<IAccountCreationProgressReporter, Task> operation)
        => operation(new Reporter());

    private sealed class Reporter : IAccountCreationProgressReporter
    {
        public CancellationToken CancellationToken => System.Threading.CancellationToken.None;

        public void SetStatus(string message)
        {
        }
    }
}
