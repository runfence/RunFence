namespace RunFence.Account.UI;

/// <summary>
/// Handles container-specific context menu actions (create, edit, delete container; copy/open profile;
/// launch folder browser; copy SID) for the accounts grid.
/// </summary>
public class ContainerContextMenuHandler(AccountContainerOrchestrator containerHandler)
{
    private DataGridView _grid = null!;

    public event Action? DataChangedAndRefresh;

    public void Initialize(DataGridView grid)
    {
        _grid = grid;
    }

    public void CreateContainer()
    {
        containerHandler.CreateContainer(_grid.FindForm(), () => DataChangedAndRefresh?.Invoke());
    }

    public void EditContainer(ContainerRow containerRow)
    {
        containerHandler.EditContainer(containerRow, _grid.FindForm(), () => DataChangedAndRefresh?.Invoke());
    }

    public void DeleteContainer(ContainerRow containerRow)
    {
        containerHandler.DeleteContainer(containerRow, () => DataChangedAndRefresh?.Invoke());
    }

    public void CopyContainerProfilePath(ContainerRow containerRow)
    {
        AccountContainerOrchestrator.CopyContainerProfilePath(containerRow);
    }

    public void OpenContainerProfileFolder(ContainerRow containerRow)
    {
        containerHandler.OpenContainerProfileFolder(containerRow);
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