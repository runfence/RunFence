using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.SidMigration;
using RunFence.SidMigration.UI.Forms;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class SidMigrationInAppStepDesignerTests
{
    [Fact]
    public void DescriptionSummaryListApplyAndResult_DoNotOverlap_AfterScaling()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var session = new SessionContext
{
                Database = new AppDatabase(),
                CredentialStore = new CredentialStore(),
            }.WithOwnedPinDerivedKey(TestSecretFactory.Create(32));
            using var step = new SidMigrationInAppStep(
                null!,
                session,
                [],
                [],
                Mock.Of<IProfilePathResolver>());

            step.Scale(new SizeF(1.75F, 1.75F));
            step.PerformLayout();

            var labels = step.Controls.OfType<Label>().ToList();
            var description = labels.Single(label => label.Text.StartsWith("The disk pass is finished", StringComparison.Ordinal));
            var summary = labels.Single(label => label.Text.StartsWith("No in-app references", StringComparison.Ordinal));
            var result = labels.Single(label => string.IsNullOrEmpty(label.Text));
            var listBox = step.Controls.OfType<ListBox>().Single();
            var apply = step.Controls.OfType<Button>().Single(button => button.Text == "Apply");

            Assert.True(description.Bottom <= summary.Top);
            Assert.True(summary.Bottom <= listBox.Top);
            Assert.True(listBox.Bottom <= apply.Top);
            Assert.True(apply.Bottom <= result.Top);
        });
    }
}
