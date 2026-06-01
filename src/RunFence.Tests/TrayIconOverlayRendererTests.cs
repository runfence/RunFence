using RunFence.TrayIcon;
using Xunit;
using System.Drawing;

namespace RunFence.Tests;

public class TrayIconOverlayRendererTests
{
    [Theory]
    [InlineData(255, 0, 0)]
    [InlineData(0, 128, 0)]
    [InlineData(0, 0, 255)]
    public void CreateOverlayIcon_AppliesMarkerColor(int r, int g, int b)
    {
        var renderer = new TrayIconOverlayRenderer();
        using var baseIcon = CreateTestIcon(Color.Black);
        using var overlayIcon = renderer.CreateOverlayIcon(baseIcon, Color.FromArgb(r, g, b));

        using var overlayBitmap = new Bitmap(overlayIcon.ToBitmap());
        var center = GetBadgeCenter(overlayBitmap);
        var markerColor = overlayBitmap.GetPixel(center.X, center.Y);

        Assert.Equal(Color.FromArgb(r, g, b).ToArgb(), markerColor.ToArgb());
    }

    [Theory]
    [InlineData(32, 32, 9, 9, 13)]
    [InlineData(16, 16, 4, 4, 8)]
    public void CalculateBadgeBounds_UsesLargeCenteredBadge(
        int width,
        int height,
        int expectedX,
        int expectedY,
        int expectedDiameter)
    {
        var bounds = TrayIconOverlayRenderer.CalculateBadgeBounds(new Size(width, height));

        Assert.Equal(new Rectangle(expectedX, expectedY, expectedDiameter, expectedDiameter), bounds);
    }

    [Fact]
    public void CreateOverlayIcon_HasContrastOutlineAroundBadge()
    {
        var renderer = new TrayIconOverlayRenderer();
        using var baseIcon = CreateTestIcon(Color.Black);
        var markerColor = Color.Crimson;

        using var overlayIcon = renderer.CreateOverlayIcon(baseIcon, markerColor);
        using var overlayBitmap = new Bitmap(overlayIcon.ToBitmap());

        var hasMarker = false;
        var hasLightOutline = false;
        for (var y = 0; y < overlayBitmap.Height; y++)
        {
            for (var x = 0; x < overlayBitmap.Width; x++)
            {
                var color = overlayBitmap.GetPixel(x, y);
                if (color.A == 0)
                    continue;

                if (color.ToArgb() == markerColor.ToArgb())
                    hasMarker = true;

                if (color != markerColor && color != Color.Black && color.A > 100 && color.R > 180 && color.G > 180 && color.B > 180)
                    hasLightOutline = true;
            }
        }

        Assert.True(hasMarker);
        Assert.True(hasLightOutline);
    }

    [Fact]
    public void CreateOverlayIcon_ReturnsOwnedIconIndependentlyOfTempHicon()
    {
        var renderer = new TrayIconOverlayRenderer();
        var baseIcon = CreateTestIcon(Color.DarkSlateGray);

        using var overlayIcon = renderer.CreateOverlayIcon(baseIcon, Color.Gold);

        using var overlayBitmap = new Bitmap(overlayIcon.ToBitmap());
        baseIcon.Dispose();
        Assert.NotEqual(0, overlayBitmap.Width);
        Assert.NotEqual(0, overlayBitmap.Height);
    }

    private static Icon CreateTestIcon(Color fillColor)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(fillColor);
        }

        var hIcon = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(hIcon).Clone();
        }
        finally
        {
            TrayIconOverlayNative.DestroyIcon(hIcon);
        }
    }

    private static Point GetBadgeCenter(Image image)
    {
        var bounds = TrayIconOverlayRenderer.CalculateBadgeBounds(image.Size);
        return new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
    }
}
