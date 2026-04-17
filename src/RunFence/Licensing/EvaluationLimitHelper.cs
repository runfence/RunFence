using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Licensing;

public class EvaluationLimitHelper(IEvaluationLimitPrompt prompt, ILicenseService licenseService) : IEvaluationLimitHelper
{
    public int CountCredentialsExcludingCurrent(IEnumerable<CredentialEntry> credentials)
    {
        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        return credentials.Count(c => !string.Equals(c.Sid, currentSid, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc/>
    public bool CheckCredentialLimit(List<CredentialEntry> credentials,
        IWin32Window? owner = null, string? extraMessage = null)
    {
        var credCount = CountCredentialsExcludingCurrent(credentials);
        if (licenseService.CanAddCredential(credCount))
            return true;

        var msg = licenseService.GetRestrictionMessage(EvaluationFeature.Credentials, credCount)!;
        if (!string.IsNullOrEmpty(extraMessage))
            msg += "\r\n\r\n" + extraMessage;
        prompt.ShowLimitMessage(msg, owner);
        return false;
    }
}
