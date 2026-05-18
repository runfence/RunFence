using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Wizard.Templates;

public class GamingExistingAccountPreparationService(
    WizardLicenseChecker licenseChecker,
    IAccountCredentialManager credentialManager,
    IGamingLogonBlockHelper logonBlockHelper,
    ISidResolver sidResolver)
{
    public bool Prepare(
        SessionContext session,
        string sid,
        ProtectedString? collectedPassword,
        IWizardProgressReporter progress)
    {
        var resolvedName = session.Database.SidNames.GetValueOrDefault(sid) ?? sidResolver.TryResolveName(sid) ?? sid;
        var username = SidNameResolver.ExtractUsername(resolvedName);
        logonBlockHelper.CheckAndPromptLogonUnblock(sid, username, null, progress);

        var willAddCredential = collectedPassword != null;

        if (!licenseChecker.CheckCanAddCredential(session, progress, willAddCredential))
            return false;

        if (collectedPassword != null)
        {
            var (success, _, error) = credentialManager.AddNewCredential(
                sid,
                collectedPassword,
                session.CredentialStore,
                session.PinDerivedKey);
            if (!success && error != null)
                progress.ReportError($"Credential: {error}");
        }

        return true;
    }
}
