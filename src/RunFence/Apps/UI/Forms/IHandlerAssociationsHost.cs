using RunFence.Core.Models;

namespace RunFence.Apps.UI.Forms;

public interface IHandlerAssociationsHost
{
    void RefreshMappings();
    AppEntry? GetSelectedApp();
    HandlerAssociationMode GetCurrentAssociationMode();
}
