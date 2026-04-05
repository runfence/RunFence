using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Licensing;

public static class EvaluationLimitHelper
{
    public static int CountCredentialsExcludingCurrent(IEnumerable<CredentialEntry> credentials)
    {
        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        return credentials.Count(c => !string.Equals(c.Sid, currentSid, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Checks whether adding another credential is allowed by the license.
    /// Returns true if allowed, false if the limit was hit (shows a message box).
    /// Pass <paramref name="extraMessage"/> to append context-specific guidance (e.g. how to remove credentials).
    /// </summary>
    public static bool CheckCredentialLimit(ILicenseService? licenseService, List<CredentialEntry> credentials,
        IWin32Window? owner = null, string? extraMessage = null)
    {
        if (licenseService == null)
            return true;

        var credCount = CountCredentialsExcludingCurrent(credentials);
        if (licenseService.CanAddCredential(credCount))
            return true;

        var msg = licenseService.GetRestrictionMessage(EvaluationFeature.Credentials, credCount);
        if (!string.IsNullOrEmpty(extraMessage))
            msg += "\r\n\r\n" + extraMessage;
        MessageBox.Show(owner, msg, "License Limit", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }
}