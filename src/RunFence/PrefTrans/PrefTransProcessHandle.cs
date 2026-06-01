using RunFence.Launch.Tokens;

namespace RunFence.PrefTrans;

internal sealed class PrefTransProcessHandle(ProcessInfo process) : IPrefTransProcessHandle
{
    public bool WaitForExit(int milliseconds) => process.WaitForExit(milliseconds);

    public void Kill(int exitCode) => process.Kill(exitCode);

    public int ExitCode => process.ExitCode;

    public void Dispose() => process.Dispose();
}
