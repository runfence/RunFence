namespace RunFence.TrayIcon;

public interface ITrayOwner
{
    Task TryShowWindowAsync();
}