using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using RunFence.ForegroundMarker;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public sealed class ForegroundMarkerWindowTests
{
    [Theory]
    [InlineData(true, 100)]
    [InlineData(false, 98)]
    public void CalculateMarkerBounds_UsesTwoPixelStripeAtExpectedHorizontalPosition(bool renderInsideLeftEdge, int expectedLeft)
    {
        var placement = new ForegroundMarkerPlacement(new Rectangle(100, 200, 640, 480), renderInsideLeftEdge);

        var markerBounds = ForegroundMarkerWindow.CalculateMarkerBounds(placement);

        Assert.Equal(expectedLeft, markerBounds.Left);
        Assert.Equal(200, markerBounds.Top);
        Assert.Equal(2, markerBounds.Width);
        Assert.Equal(480, markerBounds.Height);
    }

    [Fact]
    public void Show_UsesWindowAboveTargetAndCalculatedBounds()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var nativeMethods = new FakeForegroundMarkerNativeMethods
            {
                PreviousWindow = (IntPtr)555
            };
            using var markerWindow = new ForegroundMarkerWindow(nativeMethods);

            markerWindow.Show((IntPtr)1234, new Rectangle(100, 200, 300, 400), renderInsideLeftEdge: false, ForegroundPrivilegeMarkerPalette.Isolated);

            var setWindowPosCall = Assert.Single(nativeMethods.SetWindowPosCalls);
            Assert.Equal((IntPtr)555, setWindowPosCall.InsertAfter);
            Assert.Equal(98, setWindowPosCall.X);
            Assert.Equal(200, setWindowPosCall.Y);
            Assert.Equal(2, setWindowPosCall.Width);
            Assert.Equal(400, setWindowPosCall.Height);
            Assert.Equal(0x0050u, setWindowPosCall.Flags);
        });
    }

    [Fact]
    public void Hide_HidesExistingHandleWithoutDestroyingIt()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var nativeMethods = new FakeForegroundMarkerNativeMethods();
            using var markerWindow = new ForegroundMarkerWindow(nativeMethods);

            markerWindow.Show((IntPtr)4321, new Rectangle(100, 200, 300, 400), renderInsideLeftEdge: true, ForegroundPrivilegeMarkerPalette.LowIntegrity);
            var handle = markerWindow.Handle;

            markerWindow.Hide();

            Assert.Equal(handle, markerWindow.Handle);
            Assert.Contains(nativeMethods.ShowWindowCalls, call => call.Command == 0);
        });
    }

    [Fact]
    public void Show_CreatesWindowWithRequiredExtendedStyles()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var markerWindow = new ForegroundMarkerWindow(new FakeForegroundMarkerNativeMethods());

            markerWindow.Show((IntPtr)1234, new Rectangle(100, 200, 300, 400), renderInsideLeftEdge: true, ForegroundPrivilegeMarkerPalette.Basic);

            var extendedStyle = GetWindowLongPtr(markerWindow.Handle, -20).ToInt64();

            Assert.NotEqual(IntPtr.Zero, markerWindow.Handle);
            Assert.NotEqual(0, extendedStyle & 0x00080000L);
            Assert.NotEqual(0, extendedStyle & 0x00000020L);
            Assert.NotEqual(0, extendedStyle & 0x08000000L);
            Assert.NotEqual(0, extendedStyle & 0x00000080L);
            Assert.NotEqual(0, extendedStyle & 0x00000008L);
        });
    }

    [Fact]
    public void Show_WindowHitTestingReturnsTransparent()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var markerWindow = new ForegroundMarkerWindow(new FakeForegroundMarkerNativeMethods());

            markerWindow.Show((IntPtr)1234, new Rectangle(100, 200, 300, 400), renderInsideLeftEdge: true, ForegroundPrivilegeMarkerPalette.Basic);

            var result = SendMessage(markerWindow.Handle, 0x0084, IntPtr.Zero, IntPtr.Zero);

            Assert.Equal(new IntPtr(-1), result);
        });
    }

    [Fact]
    public void Show_ZeroTargetWindow_HidesWithoutRepositioning()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var nativeMethods = new FakeForegroundMarkerNativeMethods();
            using var markerWindow = new ForegroundMarkerWindow(nativeMethods);

            markerWindow.Show(IntPtr.Zero, new Rectangle(100, 200, 300, 400), renderInsideLeftEdge: true, ForegroundPrivilegeMarkerPalette.Basic);

            Assert.Empty(nativeMethods.SetWindowPosCalls);
            Assert.Empty(nativeMethods.ShowWindowCalls);
        });
    }

    [Fact]
    public void Show_NoWindowAboveNonTopmostTarget_UsesNotTopmostFallback()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var nativeMethods = new FakeForegroundMarkerNativeMethods();
            using var markerWindow = new ForegroundMarkerWindow(nativeMethods);

            markerWindow.Show((IntPtr)1234, new Rectangle(100, 200, 300, 400), renderInsideLeftEdge: true, ForegroundPrivilegeMarkerPalette.Basic);

            var setWindowPosCall = Assert.Single(nativeMethods.SetWindowPosCalls);
            Assert.Equal(new IntPtr(-2), setWindowPosCall.InsertAfter);
        });
    }

    [Fact]
    public void Show_NoWindowAboveTopmostTarget_UsesTopmostFallback()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var nativeMethods = new FakeForegroundMarkerNativeMethods
            {
                WindowExStyle = 0x00000008
            };
            using var markerWindow = new ForegroundMarkerWindow(nativeMethods);

            markerWindow.Show((IntPtr)1234, new Rectangle(100, 200, 300, 400), renderInsideLeftEdge: true, ForegroundPrivilegeMarkerPalette.Basic);

            var setWindowPosCall = Assert.Single(nativeMethods.SetWindowPosCalls);
            Assert.Equal(new IntPtr(-1), setWindowPosCall.InsertAfter);
        });
    }

    [Fact]
    public void Show_WhenMarkerWindowIsImmediatelyAboveTarget_SkipsItAndUsesNextWindow()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var nativeMethods = new FakeForegroundMarkerNativeMethods();
            using var markerWindow = new ForegroundMarkerWindow(nativeMethods);

            markerWindow.Show((IntPtr)1234, new Rectangle(100, 200, 300, 400), renderInsideLeftEdge: true, ForegroundPrivilegeMarkerPalette.Basic);
            nativeMethods.SetWindowPosCalls.Clear();
            nativeMethods.GetPreviousWindowOverride = hwnd => hwnd == (IntPtr)1234
                ? markerWindow.Handle
                : (IntPtr)777;

            markerWindow.Hide();
            markerWindow.Show((IntPtr)1234, new Rectangle(100, 200, 300, 400), renderInsideLeftEdge: true, ForegroundPrivilegeMarkerPalette.Basic);

            var setWindowPosCall = Assert.Single(nativeMethods.SetWindowPosCalls);
            Assert.Equal((IntPtr)777, setWindowPosCall.InsertAfter);
        });
    }

    private sealed class FakeForegroundMarkerNativeMethods : IForegroundMarkerNativeMethods
    {
        public List<SetWindowPosCall> SetWindowPosCalls { get; } = [];
        public List<ShowWindowCall> ShowWindowCalls { get; } = [];
        public IntPtr PreviousWindow { get; init; }
        public long WindowExStyle { get; init; }
        public Func<IntPtr, IntPtr>? GetPreviousWindowOverride { get; set; }

        public IntPtr GetAncestor(IntPtr hwnd, uint gaFlags) => hwnd;
        public IntPtr GetPreviousWindow(IntPtr hwnd) => GetPreviousWindowOverride?.Invoke(hwnd) ?? PreviousWindow;
        public bool IsWindow(IntPtr hwnd) => hwnd != IntPtr.Zero;
        public bool IsIconic(IntPtr hwnd) => false;
        public IntPtr TopmostInsertAfter => new(-1);
        public IntPtr NotTopmostInsertAfter => new(-2);
        public bool TryGetWindowStyle(IntPtr hwnd, out long windowStyle)
        {
            windowStyle = 0;
            return true;
        }

        public bool TryGetWindowExStyle(IntPtr hwnd, out long windowExStyle)
        {
            windowExStyle = WindowExStyle;
            return true;
        }

        public bool TryGetWindowRect(IntPtr hwnd, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            return false;
        }

        public bool TryGetWindowCloaked(IntPtr hwnd, out bool isCloaked)
        {
            isCloaked = false;
            return true;
        }

        public bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags)
        {
            SetWindowPosCalls.Add(new SetWindowPosCall(hwnd, insertAfter, x, y, cx, cy, flags));
            return true;
        }

        public bool ShowWindow(IntPtr hwnd, int command)
        {
            ShowWindowCalls.Add(new ShowWindowCall(hwnd, command));
            return true;
        }

        public bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase) => true;
        public bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags) => true;
    }

    private sealed record SetWindowPosCall(
        IntPtr Hwnd,
        IntPtr InsertAfter,
        int X,
        int Y,
        int Width,
        int Height,
        uint Flags);

    private sealed record ShowWindowCall(IntPtr Hwnd, int Command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
}
