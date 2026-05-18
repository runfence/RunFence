using System.Runtime.InteropServices;
using RunFence.Infrastructure;
using RunFence.Tests.Helpers;
using RunFence.UI.Forms;
using Xunit;

namespace RunFence.Tests;

public class ContextHelpControllerInteractionTests
{
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmMouseMove = 0x0200;
    [Fact]
    public void HelpButtonAndOverlay_SelectingHelpButtonOrTarget_CreatesDismissablePopupState()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new TestContextHelpForm();
            StaTestHelper.CreateControlTree(form);

            ActivateHelpMode(form);
            ClickOverlayAt(form, WaitForHelpButton(form));
            Assert.Null(FindOverlay(form));
            Assert.False(PostEscapeReachesForm(form));
            Assert.True(PostEscapeReachesForm(form));

            ActivateHelpMode(form);
            ClickOverlayAt(form, form.TargetButton);
            Assert.Null(FindOverlay(form));
            Assert.False(PostEscapeReachesForm(form));
            Assert.True(PostEscapeReachesForm(form));
        });
    }

    [Fact]
    public void EscapeDeactivateAndVisibleHidden_TearDownOverlayAndPopup()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new TestContextHelpForm();
            StaTestHelper.CreateControlTree(form);

            OpenPopupForTarget(form);
            Assert.True(PostClickAwayReachesForm(form));
            Assert.True(PostEscapeReachesForm(form));

            OpenPopupForTarget(form);
            Assert.False(PostEscapeReachesForm(form));
            Assert.True(PostEscapeReachesForm(form));

            ActivateHelpMode(form);
            Assert.False(PostEscapeReachesForm(form));
            StaTestHelper.PumpUntil(() => FindOverlay(form) == null, timeoutMessage: "Timed out waiting for help mode to exit on Escape.");
            Assert.True(PostEscapeReachesForm(form));

            ActivateHelpMode(form);
            form.RaiseDeactivateForTest();
            Assert.Null(FindOverlay(form));
            Assert.True(PostEscapeReachesForm(form));

            OpenPopupForTarget(form);
            form.RaiseVisibleChangedForTest();
            Assert.Null(FindOverlay(form));
            Assert.True(PostEscapeReachesForm(form));
        });
    }

    private static void ActivateHelpMode(TestContextHelpForm form)
    {
        var helpButton = WaitForHelpButton(form);
        ClickControl(helpButton, new Point(6, 6));

        StaTestHelper.PumpUntil(() => FindOverlay(form) != null, timeoutMessage: "Timed out waiting for overlay activation.");
    }

    private static void OpenPopupForTarget(TestContextHelpForm form)
    {
        ActivateHelpMode(form);
        ClickOverlayAt(form, form.TargetButton);
    }

    private static void ClickOverlayAt(TestContextHelpForm form, Control target)
    {
        var overlay = WaitForOverlay(form);
        var overlayPoint = overlay.PointToClient(target.PointToScreen(new Point(target.Width / 2, target.Height / 2)));
        var lParam = MakeLParam(overlayPoint.X, overlayPoint.Y);
        _ = SendMessage(overlay.Handle, WmLButtonDown, IntPtr.Zero, (IntPtr)lParam);
        _ = SendMessage(overlay.Handle, WmLButtonUp, IntPtr.Zero, (IntPtr)lParam);
        Application.DoEvents();
    }

    private static void ClickControl(Control control, Point location)
    {
        var lParam = MakeLParam(location.X, location.Y);
        _ = SendMessage(control.Handle, WmLButtonDown, IntPtr.Zero, (IntPtr)lParam);
        _ = SendMessage(control.Handle, WmLButtonUp, IntPtr.Zero, (IntPtr)lParam);
        Application.DoEvents();
    }

    private static bool PostEscapeReachesForm(TestContextHelpForm form)
    {
        var before = form.EscapeKeyDownCount;
        PostMessage(form.Handle, WindowNative.WM_KEYDOWN, (IntPtr)Keys.Escape);
        return form.EscapeKeyDownCount > before;
    }

    private static bool PostClickAwayReachesForm(TestContextHelpForm form)
    {
        var before = form.LeftMouseDownCount;
        var lParam = MakeLParam(1, 1);
        _ = WindowNative.PostMessage(form.Handle, WmMouseMove, IntPtr.Zero, (IntPtr)lParam);
        _ = WindowNative.PostMessage(form.Handle, WmLButtonDown, IntPtr.Zero, (IntPtr)lParam);
        DrainPostedMessages();
        return form.LeftMouseDownCount > before;
    }

    private static void PostMessage(IntPtr handle, uint message, IntPtr wParam)
    {
        _ = WindowNative.PostMessage(handle, message, wParam, IntPtr.Zero);
        DrainPostedMessages();
    }

    private static ContextHelpOverlay WaitForOverlay(Control root)
    {
        ContextHelpOverlay? overlay = null;
        StaTestHelper.PumpUntil(() =>
        {
            overlay = FindOverlay(root);
            return overlay != null;
        }, timeoutMessage: "Timed out waiting for context-help overlay.");
        return overlay!;
    }

    private static ContextHelpButton WaitForHelpButton(Control root)
    {
        ContextHelpButton? button = null;
        StaTestHelper.PumpUntil(() =>
        {
            button = root.Controls.Find("_contextHelpButton", searchAllChildren: true)
                .OfType<ContextHelpButton>()
                .SingleOrDefault();
            return button != null;
        }, timeoutMessage: "Timed out waiting for context-help button installation.");
        return button!;
    }

    private static ContextHelpOverlay? FindOverlay(Control root)
        => EnumerateControls(root).OfType<ContextHelpOverlay>().SingleOrDefault();

    private static void DrainPostedMessages()
    {
        Application.DoEvents();
        Application.DoEvents();
    }

    private static int MakeLParam(int x, int y)
        => (y << 16) | (x & 0xFFFF);

    private static IEnumerable<Control> EnumerateControls(Control root)
    {
        yield return root;

        foreach (Control child in root.Controls)
        {
            foreach (var nested in EnumerateControls(child))
                yield return nested;
        }
    }

    private sealed class TestContextHelpForm : ContextHelpForm
    {
        public TestContextHelpForm()
        {
            ClientSize = new Size(360, 240);
            TargetButton = new Button
            {
                Location = new Point(24, 32),
                Size = new Size(120, 32),
                Text = "Target"
            };

            Controls.Add(TargetButton);
            SetContextHelp(TargetButton, "target-help");
        }

        public Button TargetButton { get; }
        public int EscapeKeyDownCount { get; private set; }
        public int LeftMouseDownCount { get; private set; }

        public void RaiseDeactivateForTest()
        {
            OnDeactivate(EventArgs.Empty);
            Application.DoEvents();
        }

        public void RaiseVisibleChangedForTest()
        {
            OnVisibleChanged(EventArgs.Empty);
            Application.DoEvents();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == (int)WindowNative.WM_KEYDOWN && (Keys)(nint)m.WParam == Keys.Escape)
                EscapeKeyDownCount++;

            if (m.Msg == WmLButtonDown)
                LeftMouseDownCount++;

            base.WndProc(ref m);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
