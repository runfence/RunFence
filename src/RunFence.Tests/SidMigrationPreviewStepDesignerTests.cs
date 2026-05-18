using RunFence.Core.Models;
using RunFence.SidMigration.UI.Forms;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class SidMigrationPreviewStepDesignerTests
{
    [Fact]
    public void DescriptionGridSummaryAndWarning_DoNotOverlap_AfterScaling()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var matches = Enumerable.Range(0, 12001)
                .Select(i => new SidMigrationMatch
                {
                    Path = $"C:\\Path{i}",
                    IsDirectory = false,
                    MatchType = SidMigrationMatchType.Ace,
                    AceCountByOldSid = new Dictionary<string, int> { ["S-1-5-21-test"] = 1 }
                })
                .ToList();
            using var step = new SidMigrationPreviewStep(matches);

            step.Scale(new SizeF(1.75F, 1.75F));
            step.PerformLayout();

            var labels = step.Controls.OfType<Label>().ToList();
            var description = labels.Single(label => label.Text.StartsWith("This is the last dry-run review", StringComparison.Ordinal));
            var summary = labels.Single(label => label.Text.StartsWith("Total:", StringComparison.Ordinal));
            var warning = labels.Single(label => label.Text.StartsWith("Warning:", StringComparison.Ordinal));
            var grid = step.Controls.OfType<DataGridView>().Single();

            Assert.True(description.Bottom <= grid.Top);
            Assert.True(grid.Bottom <= summary.Top);
            Assert.True(summary.Bottom <= warning.Top);
            Assert.False(description.Bounds.IntersectsWith(grid.Bounds));
            Assert.False(grid.Bounds.IntersectsWith(summary.Bounds));
            Assert.False(summary.Bounds.IntersectsWith(warning.Bounds));
        });
    }
}
