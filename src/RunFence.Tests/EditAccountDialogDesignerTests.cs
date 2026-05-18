using System.Drawing;
using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Account.UI.Forms;
using RunFence.Infrastructure;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class EditAccountDialogDesignerTests
{
    [Fact]
    public void BottomLayout_UsesNonOverlappingCells_AtDefaultScale()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var dialog = CreateDialog();
            AssertBottomLayout(dialog, requireBaseButtonHeight: true);
        });
    }

    [Fact]
    public void BottomLayout_UsesNonOverlappingCells_AfterScaling()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var dialog = CreateDialog();
            dialog.Scale(new SizeF(1.75F, 1.75F));
            dialog.PerformLayout();
            AssertBottomLayout(dialog, requireBaseButtonHeight: false);
        });
    }

    private static EditAccountDialog CreateDialog()
    {
        return new EditAccountDialog(
            Mock.Of<ILocalGroupMembershipService>(),
            Mock.Of<IAccountLoginRestrictionService>(),
            Mock.Of<IAccountLsaRestrictionService>(),
            null!,
            null!,
            null!);
    }

    private static void AssertBottomLayout(EditAccountDialog dialog, bool requireBaseButtonHeight)
    {
        var bottomLayout = dialog.Controls.OfType<TableLayoutPanel>().Single();
        dialog.PerformLayout();
        bottomLayout.PerformLayout();
        var deleteButton = FindButton(dialog, "Delete Account");
        var okButton = FindButton(dialog, "OK");
        var cancelButton = FindButton(dialog, "Cancel");
        var statusLabel = bottomLayout.Controls.OfType<Label>().Single();

        Assert.Equal(0, bottomLayout.GetColumn(deleteButton));
        Assert.Equal(1, bottomLayout.GetColumn(statusLabel));
        Assert.Equal(2, bottomLayout.GetColumn(okButton));
        Assert.Equal(3, bottomLayout.GetColumn(cancelButton));

        var columnWidths = bottomLayout.GetColumnWidths();
        Assert.Equal(4, columnWidths.Length);
        Assert.True(columnWidths[0] >= deleteButton.Width);
        Assert.True(deleteButton.Right <= statusLabel.Left,
            $"Delete/status overlap: delete={deleteButton.Bounds}, status={statusLabel.Bounds}");
        Assert.True(statusLabel.Right <= okButton.Left,
            $"Status/OK overlap: status={statusLabel.Bounds}, ok={okButton.Bounds}");
        Assert.True(okButton.Right <= cancelButton.Left,
            $"OK/cancel overlap: ok={okButton.Bounds}, cancel={cancelButton.Bounds}");
        Assert.False(deleteButton.Bounds.IntersectsWith(statusLabel.Bounds));
        Assert.False(statusLabel.Bounds.IntersectsWith(okButton.Bounds));
        Assert.False(okButton.Bounds.IntersectsWith(cancelButton.Bounds));

        if (requireBaseButtonHeight)
        {
            Assert.Equal(28, deleteButton.Height);
            Assert.Equal(28, okButton.Height);
            Assert.Equal(28, cancelButton.Height);
        }
        else
        {
            Assert.True(deleteButton.Height >= 28);
            Assert.True(okButton.Height >= 28);
            Assert.True(cancelButton.Height >= 28);
        }
    }

    private static Button FindButton(Control parent, string text)
        => parent.Controls
            .OfType<Control>()
            .SelectMany(GetSelfAndDescendants)
            .OfType<Button>()
            .Single(button => string.Equals(button.Text, text, StringComparison.Ordinal));

    private static IEnumerable<Control> GetSelfAndDescendants(Control control)
    {
        yield return control;
        foreach (Control child in control.Controls)
        {
            foreach (var nested in GetSelfAndDescendants(child))
                yield return nested;
        }
    }
}
