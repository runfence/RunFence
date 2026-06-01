namespace RunFence.PrefTrans;

internal interface IPrefTransProcessHandle : IDisposable
{
    bool WaitForExit(int milliseconds);
    void Kill(int exitCode);
    int ExitCode { get; }
}
