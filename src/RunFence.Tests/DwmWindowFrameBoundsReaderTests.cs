using System.Drawing;
using RunFence.ForegroundMarker;
using Xunit;

namespace RunFence.Tests;

public sealed class DwmWindowFrameBoundsReaderTests
{
    [Fact]
    public void TryGetExtendedFrameBounds_InvalidWindowHandle_ReturnsFalseAndEmptyBounds()
    {
        IWindowFrameBoundsReader reader = new DwmWindowFrameBoundsReader();

        var result = reader.TryGetExtendedFrameBounds(IntPtr.Zero, out var bounds);

        Assert.False(result);
        Assert.Equal(Rectangle.Empty, bounds);
    }
}
