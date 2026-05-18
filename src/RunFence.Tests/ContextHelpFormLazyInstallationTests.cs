using System.Runtime.InteropServices;
using RunFence.Tests.Helpers;
using RunFence.UI.Forms;
using Xunit;

namespace RunFence.Tests;

public class ContextHelpFormLazyInstallationTests
{
    [Fact]
    public void SetContextHelp_BeforeHandleCreated_InstallsContextHelpWhenHandleIsCreated()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new ContextHelpForm
            {
                ClientSize = new Size(320, 200)
            };
            var panel = new Panel
            {
                Dock = DockStyle.Fill
            };
            form.Controls.Add(panel);

            form.SetContextHelp(panel, "panel-help");

            Assert.Empty(FindHelpButtons(form));

            StaTestHelper.CreateControlTree(form);

            var helpButton = Assert.Single(FindHelpButtons(form));
            ClickControl(helpButton, new Point(6, 6));

            StaTestHelper.PumpUntil(
                () => FindOverlay(form) != null,
                timeoutMessage: "Timed out waiting for context-help overlay after handle-created installation.");
        });
    }

    [Fact]
    public void SetContextHelp_AfterHandleCreated_InstallsContextHelpImmediately()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new ContextHelpForm
            {
                ClientSize = new Size(320, 200)
            };
            var panel = new Panel
            {
                Dock = DockStyle.Fill
            };
            form.Controls.Add(panel);
            StaTestHelper.CreateControlTree(form);

            Assert.Empty(FindHelpButtons(form));

            form.SetContextHelp(panel, "panel-help");

            var helpButton = Assert.Single(FindHelpButtons(form));
            ClickControl(helpButton, new Point(6, 6));

            StaTestHelper.PumpUntil(
                () => FindOverlay(form) != null,
                timeoutMessage: "Timed out waiting for context-help overlay after post-handle installation.");
        });
    }

    private static IReadOnlyList<ContextHelpButton> FindHelpButtons(Control root)
        => root.Controls.Find("_contextHelpButton", searchAllChildren: true)
            .OfType<ContextHelpButton>()
            .ToArray();

    private static ContextHelpOverlay? FindOverlay(Control root)
        => EnumerateControls(root).OfType<ContextHelpOverlay>().SingleOrDefault();

    private static IEnumerable<Control> EnumerateControls(Control root)
    {
        yield return root;

        foreach (Control child in root.Controls)
        {
            foreach (var nested in EnumerateControls(child))
                yield return nested;
        }
    }

    private static void ClickControl(Control control, Point location)
    {
        var lParam = (location.Y << 16) | (location.X & 0xFFFF);
        _ = SendMessage(control.Handle, 0x0201, IntPtr.Zero, (IntPtr)lParam);
        _ = SendMessage(control.Handle, 0x0202, IntPtr.Zero, (IntPtr)lParam);
        Application.DoEvents();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
