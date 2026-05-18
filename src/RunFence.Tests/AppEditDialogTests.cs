using Moq;
using RunFence.Acl.UI.Forms;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI;
using RunFence.Apps.UI.Forms;
using RunFence.Tests.Helpers;
using RunFence.UI.Forms;
using System.Runtime.CompilerServices;
using System.Drawing;
using Xunit;

namespace RunFence.Tests;

public class AppEditDialogTests
{
    [Fact]
    public void DiscoverHandler_WhenDialogDisposedBeforeCompletion_DoesNotTouchDisposedControls()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var gate = new ManualResetEventSlim(false);
            var discoveryStarted = new ManualResetEventSlim(false);
            var discovery = new Mock<IShortcutDiscoveryService>();
            discovery.Setup(s => s.DiscoverApps()).Callback(() =>
            {
                discoveryStarted.Set();
                gate.Wait();
            }).Returns([]);

            using var dialog = CreateDialog(discovery.Object);
            StaTestHelper.CreateControlTree(dialog);
            var discoverTask = InvokeHandleDiscoverAsync(dialog);

            StaTestHelper.PumpUntil(
                () => discoveryStarted.IsSet,
                timeoutMessage: "Timed out waiting for app discovery to start.");
            dialog.Dispose();
            gate.Set();
            StaTestHelper.RunAsyncWithMessagePump(async () => await discoverTask);
        });
    }

    [Fact]
    public void Initialize_RegistersExpectedContextHelpTargets()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var dialog = CreateDialog();
            dialog.Initialize(
                existing: null,
                credentials: [],
                existingApps: [],
                commandContext: AppEditDialogTestsAccessor.CreateNoOpCommandContext());

            Assert.Contains(dialog.GetExplicitContextHelpControls(),
                control => HasHelp(dialog, control, ContextHelpTextCatalog.AppEdit_LauncherAccessOverride));
            Assert.Contains(dialog.GetExplicitContextHelpControls(),
                control => HasHelp(dialog, control, ContextHelpTextCatalog.App_PathPrefixes));
            Assert.Contains(dialog.GetExplicitContextHelpControls(),
                control => control is Panel && HasHelp(dialog, control, ContextHelpTextCatalog.App_EnvironmentVariables));
        });
    }

    [Fact]
    public void ApplySubmitResult_AfterPriorError_NonErrorStatusResetsToDefaultColor()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var dialog = CreateDialog();
            dialog.Initialize(
                existing: null,
                credentials: [],
                existingApps: [],
                commandContext: AppEditDialogTestsAccessor.CreateNoOpCommandContext());

            StaTestHelper.CreateControlTree(dialog);
            InvokeShowStatusError(dialog, "prior failure");
            InvokeApplySubmitResult(dialog, new AppEditDialogSubmitResult(
                DialogResult: null,
                Result: null,
                HasUnsavedMutations: false,
                StatusText: "Warning: file not found."));

            ref var statusLabel = ref GetStatusLabel(dialog);

            Assert.Equal("Warning: file not found.", statusLabel.Text);
            Assert.NotEqual(Color.Red.ToArgb(), statusLabel.ForeColor.ToArgb());
        });
    }

    private static AppEditDialog CreateDialog(IShortcutDiscoveryService? discoveryService = null)
        => AppEditDialogTestsAccessor.CreateDialogForContextHelp(discoveryService);

    private static IEnumerable<T> FindControls<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
                yield return match;

            foreach (var nested in FindControls<T>(child))
                yield return nested;
        }
    }

    private static Button FindButton(Control root, string text)
        => FindControls<Button>(root).First(control => string.Equals(control.Text, text, StringComparison.Ordinal));

    private static CheckBox FindCheckBox(Control root, string text)
        => FindControls<CheckBox>(root).First(control => string.Equals(control.Text, text, StringComparison.Ordinal));

    private static T FindControl<T>(Control root) where T : Control
        => FindControls<T>(root).Single();

    private static void AssertHelp(ContextHelpForm host, Control control, string expected)
    {
        Assert.True(host.TryGetContextHelp(control, out var actual));
        Assert.Equal(expected, actual);
    }

    private static bool HasHelp(ContextHelpForm host, Control control, string expected)
        => host.TryGetContextHelp(control, out var actual) && actual == expected;

    private static TextBox FindTextBoxBelowLabel(Control root, string labelText)
    {
        var label = FindControls<Label>(root)
            .Single(control => string.Equals(control.Text, labelText, StringComparison.Ordinal));

        return FindControls<TextBox>(root)
            .Where(control => control.Parent == label.Parent && control.Top > label.Top)
            .OrderBy(control => control.Top - label.Top)
            .First();
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "HandleOkAsync")]
    private static extern Task InvokeHandleOkAsync(AppEditDialog dialog);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "HandleDiscoverAsync")]
    private static extern Task InvokeHandleDiscoverAsync(AppEditDialog dialog);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "ApplySubmitResult")]
    private static extern void InvokeApplySubmitResult(AppEditDialog dialog, AppEditDialogSubmitResult result);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_statusLabel")]
    private static extern ref Label GetStatusLabel(AppEditDialog dialog);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "ShowStatusError")]
    private static extern void InvokeShowStatusError(AppEditDialog dialog, string? message);
}
