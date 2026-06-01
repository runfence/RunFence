using Moq;
using RunFence.Acl;
using RunFence.Tests.Helpers;
using RunFence.Wizard.Templates;
using RunFence.Wizard.UI.Forms.Steps;
using Xunit;

namespace RunFence.Tests;

public class PrepareSystemDriveStepTests
{
    [Fact]
    public void OnActivated_WhenDriveEnumerationThrows_DoesNotThrowAndLeavesGridEmpty()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var driveInfoSource = new Mock<IPrepareSystemDriveInfoSource>();
            driveInfoSource.Setup(s => s.GetNonSystemFixedDrives()).Throws(new InvalidOperationException("drive scan failed"));

            using var step = CreateStep(driveInfoSource: driveInfoSource.Object);

            var ex = Record.Exception(step.OnActivated);

            Assert.Null(ex);
            Assert.Empty(FindGrid(step).Rows.Cast<DataGridViewRow>());
        });
    }

    [Fact]
    public void OnActivated_SkipsNotReadyAndInspectionFailureDrives()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var driveInfoSource = new Mock<IPrepareSystemDriveInfoSource>();
            driveInfoSource.Setup(s => s.GetNonSystemFixedDrives()).Returns(
            [
                new PrepareSystemDriveInfo(@"D:\", IsReady: false, DriveFormat: "NTFS", TotalSize: 100),
                new PrepareSystemDriveInfo(@"E:\", IsReady: false, DriveFormat: null, TotalSize: null, InspectionError: "access denied")
            ]);

            using var step = CreateStep(driveInfoSource: driveInfoSource.Object);

            step.OnActivated();

            Assert.Empty(FindGrid(step).Rows.Cast<DataGridViewRow>());
        });
    }

    private static PrepareSystemDriveStep CreateStep(
        DriveAclReplacer? driveAclReplacer = null,
        IPrepareSystemDriveInfoSource? driveInfoSource = null)
    {
        driveAclReplacer ??= new DriveAclReplacer(
            Mock.Of<IGrantSyncService>(),
            Mock.Of<RunFence.Core.ILoggingService>(),
            AclAccessorFactory.Create());
        driveInfoSource ??= Mock.Of<IPrepareSystemDriveInfoSource>();

        return new PrepareSystemDriveStep(
            _ => { },
            new Dictionary<string, string>(),
            driveAclReplacer,
            driveInfoSource);
    }

    private static DataGridView FindGrid(PrepareSystemDriveStep step)
        => step.Controls.OfType<DataGridView>().Single();
}
