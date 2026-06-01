using RunFence.Core;

namespace RunFence.RunAs.UI;

public sealed record RunAsPasswordPromptResult(
    bool Accepted,
    ProtectedString? Password,
    bool RememberPassword) : IDisposable
{
    public void Dispose() => Password?.Dispose();
}
