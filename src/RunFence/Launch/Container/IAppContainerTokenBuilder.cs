namespace RunFence.Launch.Container;

public interface IAppContainerTokenBuilder
{
    AppContainerLaunchTokenContext Build(
        IntPtr explorerToken,
        string containerSid,
        IReadOnlyList<string>? capabilities);
}
