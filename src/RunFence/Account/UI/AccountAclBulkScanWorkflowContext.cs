using RunFence.Acl.UI;
using RunFence.Infrastructure;

namespace RunFence.Account.UI;

public sealed class AccountAclBulkScanWorkflowContext : IAclBulkScanWorkflowContext
{
    private const string NoKnownSidsMessage = "No known accounts to scan for.";
    private const string NoResultsMessage = "No ACL entries found for the known accounts in the selected folder.";

    private readonly IAccountsPanelContext context;
    private readonly IScanProgressReporter progressReporter;
    private readonly IAclBulkScanMessagePresenter messagePresenter;

    public AccountAclBulkScanWorkflowContext(
        IAccountsPanelContext context,
        IScanProgressReporter progressReporter,
        IAclBulkScanMessagePresenter messagePresenter)
    {
        this.context = context;
        this.progressReporter = progressReporter;
        this.messagePresenter = messagePresenter;
    }

    public IWin32Window? Owner => context.OwnerControl.FindForm();

    public string FailureLogMessage => "ACL bulk scan failed";

    public Task<HashSet<string>> GetKnownSidsAsync()
    {
        return Task.FromResult(
            context.CredentialStore.Credentials
                .Where(c => !string.IsNullOrEmpty(c.Sid))
                .Select(c => c.Sid)
                .ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    public void SetScanBusy(bool busy) => progressReporter.SetScanEnabled(!busy);

    public void SetStatusText(string text) => progressReporter.SetStatus(text);

    public DialogResult ShowResults(Form dialog) => context.ShowModal(dialog);

    public void SaveImportedResults() => context.SaveAndRefresh();

    public void ShowNoKnownSids() => messagePresenter.ShowNoKnownSids(Owner, NoKnownSidsMessage);

    public void ShowNoResults() => messagePresenter.ShowNoResults(Owner, NoResultsMessage);

    public void ShowScanFailed(Exception exception) => messagePresenter.ShowScanFailed(Owner, exception);
}
