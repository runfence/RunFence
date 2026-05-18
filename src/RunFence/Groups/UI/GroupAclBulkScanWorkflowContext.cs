using RunFence.Account;
using RunFence.Acl.UI;
using RunFence.Infrastructure;

namespace RunFence.Groups.UI;

public sealed class GroupAclBulkScanWorkflowContext : IAclBulkScanWorkflowContext
{
    private const string NoKnownSidsMessage = "No local groups to scan for.";
    private const string NoResultsMessage = "No ACL entries found for the local groups in the selected folder.";

    private readonly IWin32Window owner;
    private readonly Action<bool> setScanButtonEnabled;
    private readonly Action<string> setStatusText;
    private readonly Action saveDatabase;
    private readonly IModalCoordinator modalCoordinator;
    private readonly ILocalGroupMembershipService groupMembership;
    private readonly IAclBulkScanMessagePresenter messagePresenter;

    public GroupAclBulkScanWorkflowContext(
        IWin32Window owner,
        Action<bool> setScanButtonEnabled,
        Action<string> setStatusText,
        Action saveDatabase,
        IModalCoordinator modalCoordinator,
        ILocalGroupMembershipService groupMembership,
        IAclBulkScanMessagePresenter messagePresenter)
    {
        this.owner = owner;
        this.setScanButtonEnabled = setScanButtonEnabled;
        this.setStatusText = setStatusText;
        this.saveDatabase = saveDatabase;
        this.modalCoordinator = modalCoordinator;
        this.groupMembership = groupMembership;
        this.messagePresenter = messagePresenter;
    }

    public IWin32Window? Owner => owner;

    public string FailureLogMessage => "Group ACL bulk scan failed";

    public Task<HashSet<string>> GetKnownSidsAsync()
    {
        return Task.Run(() =>
            groupMembership.GetLocalGroups()
                .Where(g => !string.IsNullOrEmpty(g.Sid))
                .Select(g => g.Sid)
                .ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    public void SetScanBusy(bool busy) => setScanButtonEnabled(!busy);

    public void SetStatusText(string text) => setStatusText(text);

    public DialogResult ShowResults(Form dialog) => modalCoordinator.ShowModal(dialog, owner);

    public void SaveImportedResults() => saveDatabase();

    public void ShowNoKnownSids() => messagePresenter.ShowNoKnownSids(owner, NoKnownSidsMessage);

    public void ShowNoResults() => messagePresenter.ShowNoResults(owner, NoResultsMessage);

    public void ShowScanFailed(Exception exception) => messagePresenter.ShowScanFailed(owner, exception);
}
