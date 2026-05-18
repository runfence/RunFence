using RunFence.Infrastructure;
using RunFence.Launch.Container;
using RunFence.Account.UI;

namespace RunFence.Account.UI.AppContainer;

public class AppContainerProfileActions(
    IAppContainerPathProvider appContainerPathProvider,
    IClipboardTextService clipboardTextService,
    IShellHelper shellHelper)
{
    public void CopyProfilePath(ContainerRow row)
    {
        var path = appContainerPathProvider.GetContainerDataPath(row.Container.Name);
        try
        {
            clipboardTextService.SetText(path);
        }
        catch
        {
            /* best effort */
        }
    }

    public void OpenProfileFolder(ContainerRow row)
    {
        var path = appContainerPathProvider.GetContainerDataPath(row.Container.Name);
        try
        {
            shellHelper.OpenInExplorer(path);
        }
        catch
        {
            /* best effort */
        }
    }
}
