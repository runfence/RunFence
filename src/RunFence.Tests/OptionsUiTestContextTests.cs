using Autofac;
using System.Windows.Forms;
using RunFence.Core;
using RunFence.Tests.Helpers;
using RunFence.Tests.TestHelpers;
using RunFence.UI.Forms;
using Xunit;

namespace RunFence.Tests;

public class OptionsUiTestContextTests
{
    [Fact]
    public void Dispose_DisposesRetiredPinDerivedKeys()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var context = OptionsUiTestContext.Create(9);
            var contextDisposed = false;
            using var panel = context.Scope.Resolve<OptionsPanel>();
            context.SetRememberPinEnabled(false);
            StaTestHelper.CreateControlTree(panel);
            panel.SetData(context.Session);

            var callbackCount = 0;
            panel.PinDerivedKeyChanged += () => callbackCount++;

            FindCheckBox(panel, "Remember PIN").Checked = true;
            Application.DoEvents();

            Assert.Equal(1, callbackCount);
            Assert.True(context.RememberPinEnabled);
            Assert.NotSame(context.InitialPinKey, context.Session.PinDerivedKey);
            Assert.NotSame(context.InitialCurrentPinKey, context.Session.PinDerivedKey);
            Assert.False(context.Session.PinDerivedKey is SecureSecret);
            var currentPinKey = context.Session.PinDerivedKey;

            try
            {
                context.Dispose();
                contextDisposed = true;

                Assert.Throws<ObjectDisposedException>(() => context.InitialPinKey.UseSnapshot(_ => { }));
                Assert.Throws<ObjectDisposedException>(() => currentPinKey.UseSnapshot(_ => { }));
            }
            finally
            {
                if (!contextDisposed)
                    context.Dispose();
            }
        });
    }

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
