namespace RunFence.Account.UI;

public interface IInstallProcess : IDisposable
{
    bool HasExited { get; }
    int ExitCode { get; }
}
