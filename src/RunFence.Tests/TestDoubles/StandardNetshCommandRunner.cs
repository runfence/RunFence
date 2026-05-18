using RunFence.Firewall;

namespace RunFence.Tests;

public sealed class StandardNetshCommandRunner : INetshCommandRunner
{
    public Task<DynamicPortRangeCommandResult> RunAsync(string arguments) =>
        Task.FromResult(new DynamicPortRangeCommandResult(
            0,
            """
            Start Port      : 49152
            Number of Ports : 16384
            """,
            TimedOut: false,
            FailureMessage: null));
}
