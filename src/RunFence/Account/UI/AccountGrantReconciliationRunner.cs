using RunFence.Acl.Traverse;
using RunFence.Core.Models;

namespace RunFence.Account.UI;

public interface IAccountGrantReconciliationRunner
{
    Task<List<(string Sid, List<string> NewGroups)>> DetectGroupChanges();

    Task<GrantReconciliationService.ReconciliationResult> ReconcileChangedSidsAsync(
        List<(string Sid, List<string> NewGroups)> changedSids,
        IReadOnlyDictionary<string, IReadOnlyList<GrantedPathEntry>> grantsSnapshot);

    void ApplyReconciliationResult(GrantReconciliationService.ReconciliationResult result);
}

public class AccountGrantReconciliationRunner(GrantReconciliationService reconciliationService)
    : IAccountGrantReconciliationRunner
{
    public Task<List<(string Sid, List<string> NewGroups)>> DetectGroupChanges()
        => reconciliationService.DetectGroupChanges();

    public Task<GrantReconciliationService.ReconciliationResult> ReconcileChangedSidsAsync(
        List<(string Sid, List<string> NewGroups)> changedSids,
        IReadOnlyDictionary<string, IReadOnlyList<GrantedPathEntry>> grantsSnapshot)
        => Task.Run(() => reconciliationService.ReconcileChangedSids(changedSids, grantsSnapshot));

    public void ApplyReconciliationResult(GrantReconciliationService.ReconciliationResult result)
        => reconciliationService.ApplyReconciliationResult(result);
}
