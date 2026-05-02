using RunFence.DragBridge;
using RunFence.Infrastructure;
using RunFence.UI.Forms;

namespace RunFence.Startup;

/// <summary>
/// Wires DragBridge-specific event subscriptions during application startup.
/// </summary>
public class DragBridgeEventWirer(
    IUiThreadInvoker uiThreadInvoker,
    IStartupFormLifetime formLifetime,
    IApplicationDataChangeSource applicationState,
    ISessionProvider sessionProvider,
    IDragBridgeSettingsChangeSource settingsChangeSource,
    IDragBridgeService dragBridgeService) : IStartupEventWirer
{
    public void WireEvents()
    {
        applicationState.DataChanged +=
            () => uiThreadInvoker.BeginInvoke(() => dragBridgeService.SetData(sessionProvider.GetSession()));
        settingsChangeSource.DragBridgeSettingsChanged +=
            () => dragBridgeService.ApplySettings(sessionProvider.GetSession().Database.Settings);
        formLifetime.FormClosed +=
            (_, _) => dragBridgeService.Dispose();
    }
}
