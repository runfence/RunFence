using RunFence.Launch.Tokens;

namespace RunFence.PrefTrans;

internal sealed class PrefTransProcessHandleFactory : IPrefTransProcessHandleFactory
{
    public IPrefTransProcessHandle Create(ProcessInfo process) => new PrefTransProcessHandle(process);
}
