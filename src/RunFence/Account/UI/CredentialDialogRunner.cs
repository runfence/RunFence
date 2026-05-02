using RunFence.Account.UI.Forms;
using RunFence.Core.Models;
using RunFence.Wizard;

namespace RunFence.Account.UI;

public sealed class CredentialDialogRunner(Func<CredentialEditDialog> credentialEditDialogFactory) : ICredentialDialogRunner
{
    public CredentialDialogResult ShowCredentialDialog(
        CredentialEntry credentialEntry,
        IReadOnlyDictionary<string, string>? sidNames)
    {
        using var dlg = credentialEditDialogFactory();
        dlg.Initialize(existing: credentialEntry, sidNames: sidNames);
        return dlg.ShowDialog() == DialogResult.OK
            ? new CredentialDialogResult(true, dlg.Password)
            : new CredentialDialogResult(false, null);
    }
}
