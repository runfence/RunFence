using RunFence.Account.UI.AppContainer;

namespace RunFence.Account.UI;

/// <summary>
/// Handles container-specific context menu actions (create, edit, delete container; copy/open profile;
/// launch folder browser; copy SID) for the accounts grid.
/// </summary>
public class ContainerContextMenuHandler
{
    private DataGridView _grid = null!;
    private readonly AccountContainerOrchestrator _containerHandler;
    private readonly AppContainerProfileActions _profileActions;

    public event Action? DataChangedAndRefresh;

    public ContainerContextMenuHandler(
        AccountContainerOrchestrator containerHandler,
        AppContainerProfileActions profileActions)
    {
        _containerHandler = containerHandler;
        _profileActions = profileActions;
    }

    public void Initialize(DataGridView grid)
    {
        _grid = grid;
    }

    public void CreateContainer()
    {
        _containerHandler.CreateContainer(_grid.FindForm(), () => DataChangedAndRefresh?.Invoke());
    }

    public async Task EditContainer(ContainerRow containerRow)
    {
        await _containerHandler.EditContainer(containerRow, _grid.FindForm(), () => DataChangedAndRefresh?.Invoke());
    }

    public async Task DeleteContainer(ContainerRow containerRow)
    {
        await _containerHandler.DeleteContainer(containerRow, () => DataChangedAndRefresh?.Invoke());
    }

    public void CopyContainerProfilePath(ContainerRow containerRow)
    {
        _profileActions.CopyProfilePath(containerRow);
    }

    public void OpenContainerProfileFolder(ContainerRow containerRow)
    {
        _profileActions.OpenProfileFolder(containerRow);
    }

    public void OpenAclManager(ContainerRow containerRow)
    {
        _containerHandler.OpenAclManager(containerRow, _grid.FindForm());
    }

    public void OpenAclManager(AccountRow accountRow)
    {
        _containerHandler.OpenAclManager(accountRow, _grid.FindForm());
    }
}
