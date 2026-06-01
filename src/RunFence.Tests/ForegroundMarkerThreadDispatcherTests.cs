using RunFence.ForegroundMarker;
using System.Windows.Forms;
using Xunit;

namespace RunFence.Tests;

public sealed class ForegroundMarkerThreadDispatcherTests
{
    [Fact]
    public void Start_DoesNotInstallWinFormsSynchronizationContextOnMarkerThread()
    {
        using var dispatcher = new ForegroundMarkerThreadDispatcher();
        var autoInstall = WindowsFormsSynchronizationContext.AutoInstall;

        dispatcher.Start();

        SynchronizationContext? markerThreadContext = null;
        dispatcher.Invoke(() => markerThreadContext = SynchronizationContext.Current);

        Assert.Equal(autoInstall, WindowsFormsSynchronizationContext.AutoInstall);
        Assert.False(markerThreadContext is WindowsFormsSynchronizationContext);
        Assert.NotSame(markerThreadContext, markerThreadContext?.CreateCopy());
        Assert.False(markerThreadContext?.CreateCopy() is WindowsFormsSynchronizationContext);
    }

    [Fact]
    public async Task SynchronizationContextPost_RunsAsynchronouslyOnMarkerThread()
    {
        using var dispatcher = new ForegroundMarkerThreadDispatcher();
        var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        dispatcher.Start();

        bool ranInline = false;
        dispatcher.Invoke(() =>
        {
            var context = SynchronizationContext.Current!;
            var returned = false;
            context.Post(
                _ =>
                {
                    ranInline = !returned;
                    signal.SetResult(dispatcher.IsCurrentThread);
                },
                null);
            returned = true;
        });

        Assert.True(await signal.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(ranInline);
    }
}
