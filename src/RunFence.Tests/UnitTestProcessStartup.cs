using System.Runtime.CompilerServices;

namespace RunFence.Tests;

internal static class UnitTestProcessStartup
{
    [ModuleInitializer]
    internal static void Initialize()
        => AppContext.SetSwitch("RunFence.UnitTests.UseAdminOperationMocks", true);
}
