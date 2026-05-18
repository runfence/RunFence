using RunFence.Core.Models;

namespace RunFence.Licensing;

public interface IEvaluationCredentialCounter
{
    int CountCredentialsExcludingCurrent(IEnumerable<CredentialEntry> credentials);
}
