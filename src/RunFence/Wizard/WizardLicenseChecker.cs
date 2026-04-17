using RunFence.Core.Models;
using RunFence.Licensing;

namespace RunFence.Wizard;

/// <summary>
/// Centralizes evaluation license checks for wizard templates. Returns false (with error
/// reported to progress) when a limit is hit so templates can early-return immediately.
/// </summary>
public class WizardLicenseChecker(ILicenseService licenseService, IEvaluationLimitHelper evaluationLimitHelper)
{
    /// <summary>
    /// Checks whether adding another credential is within the evaluation limit.
    /// Reports an error and returns false if the limit is exceeded.
    /// Pass <paramref name="checkCredential"/> = false to skip the check entirely (e.g. when an
    /// existing account is selected and no new credential will be stored).
    /// </summary>
    public bool CheckCanAddCredential(SessionContext session, IWizardProgressReporter progress, bool checkCredential = true)
    {
        if (!checkCredential)
            return true;
        var credCount = evaluationLimitHelper.CountCredentialsExcludingCurrent(session.CredentialStore.Credentials);
        if (licenseService.CanAddCredential(credCount))
            return true;
        progress.ReportError(licenseService.GetRestrictionMessage(EvaluationFeature.Credentials, credCount)!);
        return false;
    }

    /// <summary>
    /// Checks whether adding another app entry is within the evaluation limit.
    /// Reports an error and returns false if the limit is exceeded.
    /// </summary>
    public bool CheckCanAddApp(SessionContext session, IWizardProgressReporter progress)
    {
        var appCount = session.Database.Apps.Count;
        if (licenseService.CanAddApp(appCount))
            return true;
        progress.ReportError(licenseService.GetRestrictionMessage(EvaluationFeature.Apps, appCount)!);
        return false;
    }

    /// <summary>
    /// Checks whether adding <paramref name="count"/> app entries is within the evaluation limit.
    /// Reports an error and returns false if the limit is exceeded.
    /// </summary>
    public bool CheckCanAddApps(SessionContext session, int count, IWizardProgressReporter progress)
    {
        var appCount = session.Database.Apps.Count;
        if (licenseService.CanAddApp(appCount + count - 1))
            return true;
        progress.ReportError(licenseService.GetRestrictionMessage(EvaluationFeature.Apps, appCount + count - 1)!);
        return false;
    }

    /// <summary>
    /// Checks whether creating another AppContainer is within the evaluation limit.
    /// Reports an error and returns false if the limit is exceeded.
    /// </summary>
    public bool CheckCanCreateContainer(SessionContext session, IWizardProgressReporter progress)
    {
        var containerCount = session.Database.AppContainers.Count;
        if (licenseService.CanCreateContainer(containerCount))
            return true;
        progress.ReportError(licenseService.GetRestrictionMessage(EvaluationFeature.Containers, containerCount)!);
        return false;
    }
}