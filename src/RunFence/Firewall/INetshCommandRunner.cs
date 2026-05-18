namespace RunFence.Firewall;

public interface INetshCommandRunner
{
    Task<DynamicPortRangeCommandResult> RunAsync(string arguments);
}
