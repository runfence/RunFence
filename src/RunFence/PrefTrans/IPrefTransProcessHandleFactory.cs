using RunFence.Launch.Tokens;

namespace RunFence.PrefTrans;

internal interface IPrefTransProcessHandleFactory
{
    IPrefTransProcessHandle Create(ProcessInfo process);
}
