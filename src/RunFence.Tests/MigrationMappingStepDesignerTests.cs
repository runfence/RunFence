using Moq;
using RunFence.Account;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.SidMigration;
using RunFence.SidMigration.UI.Forms;
using RunFence.Tests.Helpers;
using RunFence.UI;
using Xunit;

namespace RunFence.Tests;

public class MigrationMappingStepDesignerTests
{
    [Fact]
    public void DescriptionAndLoadingLabel_DoNotOverlap_AfterScaling()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var step = new MigrationMappingStep(
                new SessionContext(),
                Mock.Of<ISidMigrationService>(),
                Mock.Of<ILocalUserProvider>(),
                Mock.Of<ILoggingService>(),
                [],
                Mock.Of<IProfilePathResolver>(),
                Mock.Of<ISidNameCacheService>(),
                Mock.Of<IMessageBoxService>());

            step.Scale(new SizeF(1.75F, 1.75F));
            step.PerformLayout();

            var labels = step.Controls.OfType<Label>().ToList();
            var description = labels.Single(label => label.Text.StartsWith("Review each old security identity", StringComparison.Ordinal));
            var loading = labels.Single(label => label.Text == "Resolving SIDs...");

            Assert.True(description.Bottom <= loading.Top);
            Assert.False(description.Bounds.IntersectsWith(loading.Bounds));
        });
    }
}
