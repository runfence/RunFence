using System.Security;
using RunFence.Account.UI.Forms;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Wizard;

public class WizardCredentialCollector(
    ISecureDesktopRunner secureDesktopRunner,
    Func<CredentialEditDialog> credentialEditDialogFactory)
{
    /// <summary>
    /// Checks if the account already has a stored credential. If not, shows the
    /// credential edit dialog on the secure desktop to collect a password.
    /// Returns the collected password, or null if credential already exists.
    /// Throws OperationCanceledException if collection fails or user cancels.
    /// </summary>
    public SecureString? CollectIfNeeded(string sid, SessionContext session, IWizardProgressReporter progress)
    {
        bool alreadyHasCredential = session.CredentialStore.Credentials
            .Any(c => string.Equals(c.Sid, sid, StringComparison.OrdinalIgnoreCase));
        if (alreadyHasCredential)
            return null;

        SecureString? collected = null;
        Exception? dialogException = null;
        var credEntry = new CredentialEntry { Id = Guid.NewGuid(), Sid = sid };

        try
        {
            secureDesktopRunner.Run(() =>
            {
                using var dlg = credentialEditDialogFactory();
                dlg.Initialize(existing: credEntry,
                    sidNames: session.Database.SidNames);
                if (dlg.ShowDialog() == DialogResult.OK)
                    collected = dlg.Password;
            });
        }
        catch (Exception ex) { dialogException = ex; }

        if (dialogException != null)
        {
            progress.ReportError($"Credential dialog: {dialogException.Message}");
            throw new OperationCanceledException("Credential collection failed.", dialogException);
        }

        if (collected == null)
        {
            progress.ReportError("Password is required to use this account.");
            throw new OperationCanceledException("Password is required to use this account.");
        }

        return collected;
    }
}
