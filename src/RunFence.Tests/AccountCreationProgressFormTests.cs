using RunFence.Account.UI.Forms;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class AccountCreationProgressFormTests
{
    [Fact]
    public void Initialize_RegistersCancelButton()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new TestAccountCreationProgressForm();
            StaTestHelper.CreateControlTree(form);

            var cancelButton = FindControls<Button>(form).Single(control => control.Text == "Cancel");

            Assert.Same(cancelButton, form.CancelButton);
        });
    }

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

    private sealed class TestAccountCreationProgressForm : AccountCreationProgressForm;
}
