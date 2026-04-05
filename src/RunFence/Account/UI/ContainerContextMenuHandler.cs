using RunFence.Infrastructure;

namespace RunFence.Account.UI;

/// <summary>
/// Handles container-specific context menu actions (create, edit, delete container; copy/open profile;
/// launch folder browser; copy SID) for the accounts grid.
/// </summary>
public class ContainerContextMenuHandler(
    AccountContainerOrchestrator containerHandler,
    ISessionProvider sessionProvider)
{
    private DataGridView _grid = null!;

    public event Action? DataChangedAndRefresh;

    public void Initialize(DataGridView grid)
    {
        _grid = grid;
    }

    public void CreateContainer()
    {
        var session = sessionProvider.GetSession();
        containerHandler.CreateContainer(session.Database, session.CredentialStore, session.PinDerivedKey,
            _grid.FindForm(), () => DataChangedAndRefresh?.Invoke());
    }

    public void EditContainer(ContainerRow containerRow)
    {
        var session = sessionProvider.GetSession();
        containerHandler.EditContainer(containerRow, session.Database, session.CredentialStore, session.PinDerivedKey,
            _grid.FindForm(), () => DataChangedAndRefresh?.Invoke());
    }

    public void DeleteContainer(ContainerRow containerRow)
    {
        var session = sessionProvider.GetSession();
        containerHandler.DeleteContainer(containerRow, session.Database, session.CredentialStore, session.PinDerivedKey,
            () => DataChangedAndRefresh?.Invoke());
    }

    public void CopyContainerProfilePath(ContainerRow containerRow)
    {
        AccountContainerOrchestrator.CopyContainerProfilePath(containerRow);
    }

    public void OpenContainerProfileFolder(ContainerRow containerRow)
    {
        AccountContainerOrchestrator.OpenContainerProfileFolder(containerRow);
    }

    public void OpenContainerFolderBrowser(ContainerRow containerRow)
    {
        var session = sessionProvider.GetSession();
        containerHandler.LaunchFolderBrowser(containerRow, session.Database.Settings, session.Database, session.CredentialStore, session.PinDerivedKey);
    }

    public void OpenAclManager(ContainerRow containerRow)
    {
        containerHandler.OpenAclManager(containerRow, _grid.FindForm());
    }

    public void OpenAclManager(AccountRow accountRow)
    {
        containerHandler.OpenAclManager(accountRow, _grid.FindForm());
    }
}