using Autofac.Features.OwnedInstances;
using RunFence.Acl.UI.Forms;

namespace RunFence.Account.UI;

/// <summary>
/// Opens the ACL Manager dialog for accounts, containers, and groups.
/// Extracted from <see cref="AccountContainerOrchestrator"/> to share with
/// <see cref="RunFence.Groups.UI.GroupActionOrchestrator"/>.
/// </summary>
public class AccountAclManagerLauncher(
    Func<Owned<AclManagerDialog>> dialogFactory,
    ISidNameCacheService sidNameCache)
{
    public void OpenAclManager(ContainerRow row, IWin32Window? parent)
    {
        if (string.IsNullOrEmpty(row.ContainerSid))
            return;
        OpenAclManagerInternal(row.ContainerSid, isContainer: true, row.Container.DisplayName, parent);
    }

    public void OpenAclManager(AccountRow row, IWin32Window? parent)
    {
        if (string.IsNullOrEmpty(row.Sid))
            return;
        var displayName = sidNameCache.GetDisplayName(row.Sid);
        OpenAclManagerInternal(row.Sid, isContainer: false, displayName, parent);
    }

    public void OpenAclManager(string sid, string displayName, IWin32Window? parent)
    {
        OpenAclManagerInternal(sid, isContainer: false, displayName, parent);
    }

    private void OpenAclManagerInternal(string sid, bool isContainer, string displayName, IWin32Window? parent)
    {
        using var owned = dialogFactory();
        owned.Value.Initialize(sid, isContainer, displayName);
        owned.Value.ShowDialog(parent);
    }
}