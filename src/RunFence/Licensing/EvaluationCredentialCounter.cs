using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Core.Models;

namespace RunFence.Licensing;

public class EvaluationCredentialCounter : IEvaluationCredentialCounter
{
    public int CountCredentialsExcludingCurrent(IEnumerable<CredentialEntry> credentials)
    {
        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        return credentials.Count(c => !SidComparer.SidEquals(c.Sid, currentSid));
    }
}
