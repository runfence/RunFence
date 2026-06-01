using RunFence.Core.Models;

namespace RunFence.Apps.UI;

public interface IApplicationsPanelCommandView
{
    AppEntry? GetSelectedApp();
    IWin32Window GetOwner();
    void SaveAndRefresh();
}
