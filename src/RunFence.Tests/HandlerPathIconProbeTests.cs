using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI;
using Xunit;

namespace RunFence.Tests;

public class HandlerPathIconProbeTests
{
    [Theory]
    [InlineData(".exe")]
    [InlineData(".cmd")]
    [InlineData(".bat")]
    [InlineData(".ps1")]
    public void GetIconPresence_UsesExecutableIconCountReaderForSupportedExtensions(string extension)
    {
        var probe = new HandlerPathIconProbe(new FakeExecutableIconCountReader
        {
            IconCount = 2
        });

        var presence = probe.GetIconPresence($@"C:\Apps\Test{extension}");

        Assert.Equal(HandlerPathIconPresence.HasIcon, presence);
    }

    [Fact]
    public void GetIconPresence_NoIcon_WhenReaderReturnsZero()
    {
        var probe = new HandlerPathIconProbe(new FakeExecutableIconCountReader
        {
            IconCount = 0
        });

        var presence = probe.GetIconPresence(@"C:\Apps\NoIcon.exe");

        Assert.Equal(HandlerPathIconPresence.NoIcon, presence);
    }

    [Fact]
    public void GetIconPresence_Unknown_WhenReaderCannotRead()
    {
        var probe = new HandlerPathIconProbe(new FakeExecutableIconCountReader
        {
            Throw = true
        });

        var presence = probe.GetIconPresence(@"C:\Apps\Locked.exe");

        Assert.Equal(HandlerPathIconPresence.Unknown, presence);
    }

    [Fact]
    public void GetIconPresence_Unknown_WhenReaderThrows()
    {
        var probe = new HandlerPathIconProbe(new ThrowingExecutableIconCountReader());

        var presence = probe.GetIconPresence(@"C:\Apps\Locked.exe");

        Assert.Equal(HandlerPathIconPresence.Unknown, presence);
    }

    [Fact]
    public void GetIconPresence_Unknown_ForUnsupportedExtension()
    {
        var probe = new HandlerPathIconProbe(new FakeExecutableIconCountReader
        {
            IconCount = 2
        });

        Assert.Equal(HandlerPathIconPresence.Unknown, probe.GetIconPresence(@"C:\Apps\notes.txt"));
    }

    private sealed class FakeExecutableIconCountReader : IExecutableIconCountReader
    {
        public int IconCount { get; init; }
        public bool Throw { get; init; }

        public bool TryGetIconCount(string path, out int iconCount)
        {
            if (Throw)
            {
                iconCount = 0;
                return false;
            }

            iconCount = IconCount;
            return true;
        }
    }

    private sealed class ThrowingExecutableIconCountReader : IExecutableIconCountReader
    {
        public bool TryGetIconCount(string path, out int iconCount)
        {
            iconCount = 0;
            throw new UnauthorizedAccessException("No access");
        }
    }
}
