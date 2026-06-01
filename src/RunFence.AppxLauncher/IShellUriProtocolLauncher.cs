namespace RunFence.AppxLauncher;

public interface IShellUriProtocolLauncher
{
    AppxLaunchResult Launch(string uri);
}
