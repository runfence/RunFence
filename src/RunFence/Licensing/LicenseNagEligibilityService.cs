using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Licensing;

public class LicenseNagEligibilityService(
    ISessionProvider sessionProvider,
    ISessionSaver sessionSaver,
    IEvaluationCredentialCounter credentialCounter,
    ILoggingService? log = null)
{
    public void ApplyNagEligibilityLatch()
    {
        var session = sessionProvider.GetSession();
        if (session.Database.Settings.NagEligible)
            return;
        if (session.Database.Apps.Count == 0)
            return;

        var credentialCount = credentialCounter.CountCredentialsExcludingCurrent(session.CredentialStore.Credentials);
        if (credentialCount == 0)
            return;

        session.Database.Settings.NagEligible = true;
        try
        {
            sessionSaver.SaveConfig();
        }
        catch (Exception ex)
        {
            log?.Warn($"LicenseService: failed to persist NagEligible=true: {ex.Message}");
        }
    }

    public bool IsSessionEligibleForNag(AppDatabase database)
    {
        return database.Settings.NagEligible;
    }
}
