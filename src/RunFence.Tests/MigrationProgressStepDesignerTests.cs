using RunFence.SidMigration.UI.Forms;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class MigrationProgressStepDesignerTests
{
    [Fact]
    public void DescriptionProgressStatusAndCancel_DoNotOverlap_AfterScaling()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var step = new SidMigrationDiskApplyProgressStep();
            step.Configure("Applying...", 10, showCancelButton: true);
            var descriptionLabel = GetDescriptionLabel(step);

            step.Scale(new SizeF(1.75F, 1.75F));
            step.PerformLayout();

            Assert.True(descriptionLabel.Bottom <= step.ProgressBar.Top);
            Assert.True(step.ProgressBar.Bottom <= step.StatusLabel.Top);
            Assert.True(step.StatusLabel.Bottom <= step.CancelButton.Top);
            Assert.False(descriptionLabel.Bounds.IntersectsWith(step.ProgressBar.Bounds));
            Assert.False(step.ProgressBar.Bounds.IntersectsWith(step.StatusLabel.Bounds));
            Assert.False(step.StatusLabel.Bounds.IntersectsWith(step.CancelButton.Bounds));
        });
    }

    private static Label GetDescriptionLabel(MigrationProgressStep step)
        => step.Controls.OfType<Label>().Single(control => control.Dock == DockStyle.Top);
}
