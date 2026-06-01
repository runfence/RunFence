#pragma warning disable CS9113 // Parameter 'real' is intentionally unread — RegisterDecorator constructs the real implementation for DI validation
using System.Diagnostics;
using RunFence.Launch;
using RunFence.Launch.Container;
using RunFence.Launch.Tokens;

namespace RunFence.Startup.NonElevatedMocks;

public sealed class MockAppContainerProcessLauncher(
    IAppContainerProcessLauncher real) : IAppContainerProcessLauncher
{
    // real is injected but unused — RegisterDecorator constructs the real implementation
    // to keep DI validation working; launches as current user (no AppContainer isolation) in non-elevated debug mode

    public ProcessInfo LaunchFile(ProcessLaunchTarget target, AppContainerLaunchIdentity identity)
        => ProcessInfo.FromManagedProcess(Process.Start(ProcessLaunchHelper.BuildProcessStartInfo(target)));
}
