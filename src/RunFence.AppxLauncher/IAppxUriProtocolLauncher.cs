namespace RunFence.AppxLauncher;

public interface IAppxUriProtocolLauncher
{
    AppxLaunchResult Launch(AppxUriLaunchOptions options);
}
