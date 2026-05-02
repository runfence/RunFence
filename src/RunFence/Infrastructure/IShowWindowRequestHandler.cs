namespace RunFence.Infrastructure;

/// <summary>
/// Runs the normal window show/unlock flow, equivalent to clicking the tray icon or the tray Show menu item.
/// </summary>
public interface IShowWindowRequestHandler
{
    void RequestShowWindow();
}
