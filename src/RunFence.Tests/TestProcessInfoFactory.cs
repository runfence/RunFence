using RunFence.Launch;
using RunFence.Launch.Tokens;

namespace RunFence.Tests;

public static class TestProcessInfoFactory
{
    public static ProcessInfo Empty() =>
        new(default);

    public static ProcessInfo Native(ProcessLaunchNative.PROCESS_INFORMATION nativeInfo) =>
        new(nativeInfo);
}
