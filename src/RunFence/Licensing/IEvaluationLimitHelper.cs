using RunFence.Core.Models;

namespace RunFence.Licensing;

public interface IEvaluationLimitHelper
{
    int CountCredentialsExcludingCurrent(IEnumerable<CredentialEntry> credentials);

    /// <summary>
    /// Checks whether adding another credential is allowed by the license.
    /// Returns true if allowed, false if the limit was hit (shows a message via the injected prompt).
    /// Pass <paramref name="extraMessage"/> to append context-specific guidance (e.g. how to remove credentials).
    /// </summary>
    bool CheckCredentialLimit(List<CredentialEntry> credentials,
        IWin32Window? owner = null, string? extraMessage = null);
}
