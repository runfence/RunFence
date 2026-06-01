using System.Drawing;
using System.Windows.Forms;
using Moq;
using RunFence.ForegroundMarker;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public sealed class ForegroundWindowBoundsReaderTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void ShouldRenderInsideLeftEdge_UsesMonitorLeftEdgeSpace(int leftOffset, bool expectedInside)
    {
        var monitor = Screen.PrimaryScreen ?? throw new InvalidOperationException("Primary screen is unavailable.");
        var reader = new ForegroundWindowBoundsReader(
            Mock.Of<IWindowFrameBoundsReader>(),
            new ForegroundMarkerNativeMethods(),
            new ForegroundMonitorIntersectionService());
        var bounds = new Rectangle(monitor.Bounds.Left + leftOffset, monitor.Bounds.Top + 10, 500, 300);

        var result = reader.ShouldRenderInsideLeftEdge(bounds);

        Assert.Equal(expectedInside, result);
    }

    [Fact]
    public void IsFullscreen_WhenBoundsCoverMonitor_ReturnsTrue()
    {
        var monitor = Screen.PrimaryScreen ?? throw new InvalidOperationException("Primary screen is unavailable.");
        var nativeMethods = new FakeForegroundMarkerNativeMethods
        {
            IsWindowResult = true,
            Ancestor = (IntPtr)44,
            TryGetWindowStyleResult = true,
            WindowStyle = 0
        };
        var reader = new ForegroundWindowBoundsReader(
            Mock.Of<IWindowFrameBoundsReader>(),
            nativeMethods,
            new ForegroundMonitorIntersectionService());

        var result = reader.IsFullscreen((IntPtr)44, monitor.Bounds);

        Assert.True(result);
    }

    [Fact]
    public void IsFullscreen_WhenBoundsStayInsideMonitor_ReturnsFalse()
    {
        var monitor = Screen.PrimaryScreen ?? throw new InvalidOperationException("Primary screen is unavailable.");
        var nativeMethods = new FakeForegroundMarkerNativeMethods
        {
            IsWindowResult = true,
            Ancestor = (IntPtr)45,
            TryGetWindowStyleResult = true,
            WindowStyle = 0
        };
        var reader = new ForegroundWindowBoundsReader(
            Mock.Of<IWindowFrameBoundsReader>(),
            nativeMethods,
            new ForegroundMonitorIntersectionService());
        var bounds = new Rectangle(
            monitor.Bounds.Left,
            monitor.Bounds.Top,
            monitor.Bounds.Width,
            Math.Max(1, monitor.Bounds.Height - 1));

        var result = reader.IsFullscreen((IntPtr)45, bounds);

        Assert.False(result);
    }

    [Fact]
    public void IsFullscreen_WhenWindowHasCaptionFrame_ReturnsFalse()
    {
        var monitor = Screen.PrimaryScreen ?? throw new InvalidOperationException("Primary screen is unavailable.");
        var nativeMethods = new FakeForegroundMarkerNativeMethods
        {
            IsWindowResult = true,
            Ancestor = (IntPtr)55,
            TryGetWindowStyleResult = true,
            WindowStyle = 0x00C00000L
        };
        var reader = new ForegroundWindowBoundsReader(
            Mock.Of<IWindowFrameBoundsReader>(),
            nativeMethods,
            new ForegroundMonitorIntersectionService());

        var result = reader.IsFullscreen((IntPtr)55, monitor.Bounds);

        Assert.False(result);
    }

    [Fact]
    public void ResolveTrackedTopLevelWindow_NormalizesChildHandleToTopLevelWindow()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new Form();
            using var child = new TextBox();
            form.Controls.Add(child);
            StaTestHelper.CreateControlTree(form);

            var reader = new ForegroundWindowBoundsReader(
                Mock.Of<IWindowFrameBoundsReader>(),
                new ForegroundMarkerNativeMethods(),
                new ForegroundMonitorIntersectionService());

            Assert.Equal(form.Handle, reader.ResolveTrackedTopLevelWindow(child.Handle));
        });
    }

    [Fact]
    public void ResolveTrackedTopLevelWindow_KeepsOwnedDialogAsTrackedWindow()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var owner = new Form();
            using var dialog = new Form();
            owner.AddOwnedForm(dialog);
            StaTestHelper.CreateControlTree(owner);
            StaTestHelper.CreateControlTree(dialog);

            var reader = new ForegroundWindowBoundsReader(
                Mock.Of<IWindowFrameBoundsReader>(),
                new ForegroundMarkerNativeMethods(),
                new ForegroundMonitorIntersectionService());

            Assert.Equal(dialog.Handle, reader.ResolveTrackedTopLevelWindow(dialog.Handle));
        });
    }

    [Fact]
    public void TryGetVisibleBounds_ReturnsFalseForMinimizedWindow()
    {
        var frameReader = new Mock<IWindowFrameBoundsReader>(MockBehavior.Strict);
        var nativeMethods = new FakeForegroundMarkerNativeMethods
        {
            IsWindowResult = true,
            Ancestor = (IntPtr)12,
            IsIconicResult = true
        };
        var reader = new ForegroundWindowBoundsReader(frameReader.Object, nativeMethods, new FakeMonitorIntersectionService(true));

        var result = reader.TryGetVisibleBounds((IntPtr)12, out var bounds);

        Assert.False(result);
        Assert.Equal(Rectangle.Empty, bounds);
    }

    [Fact]
    public void TryGetVisibleBounds_ReturnsFalseForCloakedWindow()
    {
        var frameReader = new Mock<IWindowFrameBoundsReader>(MockBehavior.Strict);
        var nativeMethods = new FakeForegroundMarkerNativeMethods
        {
            IsWindowResult = true,
            Ancestor = (IntPtr)13,
            IsCloaked = true
        };
        var reader = new ForegroundWindowBoundsReader(frameReader.Object, nativeMethods, new FakeMonitorIntersectionService(true));

        var result = reader.TryGetVisibleBounds((IntPtr)13, out var bounds);

        Assert.False(result);
        Assert.Equal(Rectangle.Empty, bounds);
    }

    [Fact]
    public void TryGetVisibleBounds_ReturnsFalseWhenBoundsReaderFails()
    {
        var frameReader = new Mock<IWindowFrameBoundsReader>();
        frameReader.Setup(r => r.TryGetExtendedFrameBounds((IntPtr)15, out It.Ref<Rectangle>.IsAny))
            .Returns(false);
        var nativeMethods = new FakeForegroundMarkerNativeMethods
        {
            IsWindowResult = true,
            Ancestor = (IntPtr)15,
            WindowRectResult = false
        };
        var reader = new ForegroundWindowBoundsReader(frameReader.Object, nativeMethods, new FakeMonitorIntersectionService(true));

        var result = reader.TryGetVisibleBounds((IntPtr)15, out var bounds);

        Assert.False(result);
        Assert.Equal(nativeMethods.WindowRect, bounds);
    }

    [Fact]
    public void TryGetVisibleBounds_ReturnsFalseForEmptyBounds()
    {
        var emptyBounds = Rectangle.Empty;
        var frameReader = new Mock<IWindowFrameBoundsReader>();
        frameReader.Setup(r => r.TryGetExtendedFrameBounds((IntPtr)20, out emptyBounds))
            .Returns(true);
        var nativeMethods = new FakeForegroundMarkerNativeMethods
        {
            IsWindowResult = true,
            Ancestor = (IntPtr)20
        };
        var reader = new ForegroundWindowBoundsReader(frameReader.Object, nativeMethods, new FakeMonitorIntersectionService(true));

        var result = reader.TryGetVisibleBounds((IntPtr)20, out var bounds);

        Assert.False(result);
        Assert.Equal(Rectangle.Empty, bounds);
    }

    [Fact]
    public void TryGetVisibleBounds_ReturnsFalseWhenBoundsDoNotIntersectAnyMonitor()
    {
        var frameBounds = new Rectangle(4000, 5000, 300, 200);
        var frameReader = new Mock<IWindowFrameBoundsReader>();
        frameReader.Setup(r => r.TryGetExtendedFrameBounds((IntPtr)25, out frameBounds))
            .Returns(true);
        var nativeMethods = new FakeForegroundMarkerNativeMethods
        {
            IsWindowResult = true,
            Ancestor = (IntPtr)25
        };
        var reader = new ForegroundWindowBoundsReader(frameReader.Object, nativeMethods, new FakeMonitorIntersectionService(false));

        var result = reader.TryGetVisibleBounds((IntPtr)25, out var bounds);

        Assert.False(result);
        Assert.Equal(frameBounds, bounds);
    }

    [Fact]
    public void TryGetVisibleBounds_ReturnsTrueForPartiallyOffScreenWindowWithMonitorIntersection()
    {
        var frameBounds = new Rectangle(-50, 40, 500, 300);
        var frameReader = new Mock<IWindowFrameBoundsReader>();
        frameReader.Setup(r => r.TryGetExtendedFrameBounds((IntPtr)30, out frameBounds))
            .Returns(true);
        var nativeMethods = new FakeForegroundMarkerNativeMethods
        {
            IsWindowResult = true,
            Ancestor = (IntPtr)30
        };
        var reader = new ForegroundWindowBoundsReader(frameReader.Object, nativeMethods, new FakeMonitorIntersectionService(true));

        var result = reader.TryGetVisibleBounds((IntPtr)30, out var bounds);

        Assert.True(result);
        Assert.Equal(frameBounds, bounds);
    }

    private sealed class FakeForegroundMarkerNativeMethods : IForegroundMarkerNativeMethods
    {
        public IntPtr Ancestor { get; init; }
        public bool IsWindowResult { get; init; }
        public bool IsIconicResult { get; init; }
        public bool TryGetWindowStyleResult { get; init; } = true;
        public long WindowStyle { get; init; }
        public bool WindowRectResult { get; init; } = true;
        public Rectangle WindowRect { get; init; } = new(10, 20, 300, 400);
        public bool TryGetWindowCloakedResult { get; init; } = true;
        public bool IsCloaked { get; init; }

        public IntPtr GetAncestor(IntPtr hwnd, uint gaFlags) => Ancestor;
        public IntPtr GetPreviousWindow(IntPtr hwnd) => IntPtr.Zero;
        public IntPtr TopmostInsertAfter => new(-1);
        public IntPtr NotTopmostInsertAfter => new(-2);
        public bool IsWindow(IntPtr hwnd) => IsWindowResult;
        public bool IsIconic(IntPtr hwnd) => IsIconicResult;
        public bool TryGetWindowStyle(IntPtr hwnd, out long windowStyle)
        {
            windowStyle = WindowStyle;
            return TryGetWindowStyleResult;
        }

        public bool TryGetWindowExStyle(IntPtr hwnd, out long windowExStyle)
        {
            windowExStyle = 0;
            return true;
        }

        public bool TryGetWindowRect(IntPtr hwnd, out Rectangle bounds)
        {
            bounds = WindowRect;
            return WindowRectResult;
        }

        public bool TryGetWindowCloaked(IntPtr hwnd, out bool isCloaked)
        {
            isCloaked = IsCloaked;
            return TryGetWindowCloakedResult;
        }

        public bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags) => true;
        public bool ShowWindow(IntPtr hwnd, int command) => true;
        public bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase) => true;
        public bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags) => true;
    }

    private sealed class FakeMonitorIntersectionService(bool hasIntersection) : IForegroundMonitorIntersectionService
    {
        public bool TryGetMonitorBounds(Rectangle bounds, out Rectangle monitorBounds)
        {
            monitorBounds = hasIntersection ? Screen.PrimaryScreen!.Bounds : Rectangle.Empty;
            return hasIntersection;
        }
    }
}
