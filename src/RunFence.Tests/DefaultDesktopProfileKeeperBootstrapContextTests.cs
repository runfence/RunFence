using Moq;
using RunFence.Core;
using RunFence.Launch.Tokens;
using Xunit;

namespace RunFence.Tests;

public sealed class DefaultDesktopProfileKeeperBootstrapContextTests
{
    [Fact]
    public void Run_ExecutesActionOnSeparateThreadWithoutFlowingAsyncLocalContext()
    {
        var context = new DefaultDesktopProfileKeeperBootstrapContext(Mock.Of<ILoggingService>());
        var asyncLocal = new AsyncLocal<string?> { Value = "caller" };
        var callerThreadId = Environment.CurrentManagedThreadId;

        var result = context.Run(() => (
            ThreadId: Environment.CurrentManagedThreadId,
            AsyncLocalValue: asyncLocal.Value,
            ApartmentState: Thread.CurrentThread.GetApartmentState()));

        Assert.NotEqual(callerThreadId, result.ThreadId);
        Assert.Null(result.AsyncLocalValue);
        Assert.Equal(ApartmentState.MTA, result.ApartmentState);
    }
}
