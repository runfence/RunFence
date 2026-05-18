using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Wizard;

public class WizardCredentialCollector(
    ISecureDesktopRunner secureDesktopRunner,
    ICredentialDialogRunner credentialDialogRunner,
    ISessionProvider sessionProvider)
{
    /// <summary>
    /// Checks if the account already has a stored credential. If not, shows the
    /// credential edit dialog on the secure desktop to collect a password.
    /// Returns the collected password, or null if credential already exists.
    /// Throws <see cref="OperationCanceledException"/> when the user cancels.
    /// Throws <see cref="WizardReportedException"/> for any other failure.
    /// Credential-dialog failures report the user-facing error through <paramref name="progress"/>
    /// before the wrapped exception is thrown.
    /// </summary>
    public ProtectedString? CollectCredentialForStep(string sid, IWizardProgressReporter progress)
    {
        try
        {
            var session = sessionProvider.GetSession();
            bool alreadyHasCredential = session.CredentialStore.Credentials
                .Any(c => SidComparer.SidEquals(c.Sid, sid));
            if (alreadyHasCredential)
                return null;

            var dialogCompleted = false;
            var accepted = false;
            ProtectedString? collected = null;
            Exception? dialogException = null;
            var credEntry = new CredentialEntry { Id = Guid.NewGuid(), Sid = sid };

            try
            {
                secureDesktopRunner.Run(() =>
                {
                    var result = credentialDialogRunner.ShowCredentialDialog(credEntry, session.Database.SidNames);
                    dialogCompleted = true;
                    accepted = result.Accepted;
                    if (accepted)
                        collected = result.Password;
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                dialogException = ex;
            }

            if (dialogException != null)
            {
                progress.ReportError($"Credential dialog: {dialogException.Message}");
                throw new InvalidOperationException("Credential collection failed.", dialogException);
            }

            if (!dialogCompleted)
            {
                const string error = "Credential dialog did not open.";
                progress.ReportError(error);
                throw new InvalidOperationException(error);
            }

            if (!accepted)
            {
                progress.ReportError("Password is required to use this account.");
                throw new OperationCanceledException("Password is required to use this account.");
            }

            if (collected == null)
            {
                const string error = "Credential dialog did not return a password.";
                progress.ReportError(error);
                throw new InvalidOperationException(error);
            }

            return collected;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WizardReportedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new WizardReportedException(ex.Message, ex);
        }
    }
}
