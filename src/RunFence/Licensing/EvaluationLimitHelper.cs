using RunFence.Core.Models;

namespace RunFence.Licensing;

public class EvaluationLimitHelper(
    IEvaluationLimitPrompt prompt,
    ILicenseService licenseService,
    IEvaluationCredentialCounter credentialCounter) : IEvaluationLimitHelper
{
    /// <inheritdoc/>
    public bool CheckCredentialLimit(List<CredentialEntry> credentials,
        IWin32Window? owner = null, string? extraMessage = null)
    {
        var credCount = credentialCounter.CountCredentialsExcludingCurrent(credentials);
        if (licenseService.CanAddCredential(credCount))
            return true;

        var msg = licenseService.GetRestrictionMessage(EvaluationFeature.Credentials, credCount)!;
        if (!string.IsNullOrEmpty(extraMessage))
            msg += "\r\n\r\n" + extraMessage;
        prompt.ShowLimitMessage(msg, owner);
        return false;
    }
}
