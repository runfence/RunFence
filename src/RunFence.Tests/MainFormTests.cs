using Autofac;
using Moq;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.Security;
using RunFence.Startup;
using RunFence.Tests.Helpers;
using RunFence.Tests.TestHelpers;
using RunFence.UI;
using RunFence.UI.Forms;
using Xunit;

namespace RunFence.Tests;

public class MainFormTests
{
    [Fact]
    public void NoApps_DefaultsToAccountsTab()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var context = CreateMainFormContext();
            using var form = context.Scope.Resolve<MainForm>();
            _ = form.Handle;
            Application.DoEvents();

            Assert.Equal("Accounts", FindTabControl(form).SelectedTab?.Text);
        });
    }

    [Fact]
    public void RememberPinToggle_OnOptionsPanel_UpdatesSessionPinDerivedKey()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var context = CreateMainFormContext();
            using var form = context.Scope.Resolve<MainForm>();
            _ = form.Handle;
            Application.DoEvents();
            StaTestHelper.CreateControlTree(FindControls<OptionsPanel>(form).Single());

            var rememberPinCheckBox = FindCheckBox(form, "Remember PIN");
            StaTestHelper.PumpUntil(() => !rememberPinCheckBox.Checked);

            var keyBeforeToggle = context.Session.PinDerivedKey;

            rememberPinCheckBox.Checked = true;
            Application.DoEvents();

            Assert.True(context.RememberPinEnabled);
            Assert.True(rememberPinCheckBox.Checked);
            Assert.NotSame(context.InitialCurrentPinKey, context.Session.PinDerivedKey);
            Assert.NotSame(keyBeforeToggle, context.Session.PinDerivedKey);
            Assert.False(context.Session.PinDerivedKey is SecureSecret);
            Assert.NotSame(context.InitialPinKey, context.Session.PinDerivedKey);
        });
    }

    private static OptionsUiTestContext CreateMainFormContext() => OptionsUiTestContext.Create(9);

    private static TabControl FindTabControl(Control root)
        => FindControls<TabControl>(root).Single();

    private static CheckBox FindCheckBox(Control root, string text)
        => FindControls<CheckBox>(root).First(control => control.Text == text);

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
}
