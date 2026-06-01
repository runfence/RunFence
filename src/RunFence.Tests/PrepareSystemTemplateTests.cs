using Moq;
using RunFence.Acl;
using RunFence.Acl.QuickAccess;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Wizard;
using RunFence.Wizard.Templates;
using Xunit;

namespace RunFence.Tests;

public class PrepareSystemTemplateTests
{
    private const string TestSid = "S-1-5-21-100-200-300-1001";

    [Fact]
    public async Task WarmCacheAsync_WhenDriveEnumerationThrows_DoesNotThrowAndMarksUnavailable()
    {
        var driveInfoSource = new Mock<IPrepareSystemDriveInfoSource>();
        driveInfoSource.Setup(s => s.GetNonSystemFixedDrives()).Throws(new InvalidOperationException("disk probe failed"));
        var template = CreateTemplate(driveInfoSource: driveInfoSource.Object);

        await template.WarmCacheAsync();

        Assert.False(template.IsAvailable);
    }

    [Fact]
    public void IsAvailable_WhenWarmCacheHasNotRunAndDriveEnumerationThrows_ReturnsFalse()
    {
        var driveInfoSource = new Mock<IPrepareSystemDriveInfoSource>();
        driveInfoSource.Setup(s => s.GetNonSystemFixedDrives()).Throws(new InvalidOperationException("disk probe failed"));
        var template = CreateTemplate(driveInfoSource: driveInfoSource.Object);

        var available = template.IsAvailable;

        Assert.False(available);
    }

    [Fact]
    public void IsAvailable_SkipsNotReadyAndInspectionFailureDrives()
    {
        var driveInfoSource = new Mock<IPrepareSystemDriveInfoSource>();
        driveInfoSource.Setup(s => s.GetNonSystemFixedDrives()).Returns(
        [
            new PrepareSystemDriveInfo(@"D:\", IsReady: false, DriveFormat: "NTFS", TotalSize: 100),
            new PrepareSystemDriveInfo(@"E:\", IsReady: false, DriveFormat: null, TotalSize: null, InspectionError: "access denied")
        ]);
        var template = CreateTemplate(driveInfoSource: driveInfoSource.Object);

        var available = template.IsAvailable;

        Assert.False(available);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsNotReadyDrive_ReportsWarning_AndSaves()
    {
        var driveInfoSource = new Mock<IPrepareSystemDriveInfoSource>();
        driveInfoSource.Setup(s => s.InspectDrive(@"E:\")).Returns(
            new PrepareSystemDriveInfo(@"E:\", IsReady: false, DriveFormat: "NTFS", TotalSize: 100));
        driveInfoSource.Setup(s => s.GetNonSystemFixedDrives()).Returns([]);

        var sessionSaver = new Mock<IWizardSessionSaver>();
        var template = CreateTemplate(
            sessionSaver: sessionSaver.Object,
            driveInfoSource: driveInfoSource.Object);
        template.SetSelectedDrives([(@"E:\", TestSid)]);

        var progress = new RecordingProgressReporter();
        await template.ExecuteAsync(progress);

        sessionSaver.Verify(s => s.SaveAndRefresh(), Times.Once);
        Assert.Contains(progress.Warnings, warning => warning.Contains(@"E:\", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_WhenDriveInspectionThrows_SkipsDrive_ReportsWarning_AndSaves()
    {
        var driveInfoSource = new Mock<IPrepareSystemDriveInfoSource>();
        driveInfoSource.Setup(s => s.InspectDrive(@"E:\")).Throws(new InvalidOperationException("access denied"));
        driveInfoSource.Setup(s => s.GetNonSystemFixedDrives()).Returns([]);

        var sessionSaver = new Mock<IWizardSessionSaver>();
        var template = CreateTemplate(
            sessionSaver: sessionSaver.Object,
            driveInfoSource: driveInfoSource.Object);
        template.SetSelectedDrives([(@"E:\", TestSid)]);

        var progress = new RecordingProgressReporter();
        await template.ExecuteAsync(progress);

        sessionSaver.Verify(s => s.SaveAndRefresh(), Times.Once);
        Assert.Contains(progress.Warnings, warning => warning.Contains("access denied", StringComparison.OrdinalIgnoreCase));
    }

    private static PrepareSystemTemplate CreateTemplate(
        DriveAclReplacer? replacer = null,
        IWizardSessionSaver? sessionSaver = null,
        IQuickAccessPinService? quickAccessPinService = null,
        IPrepareSystemDriveInfoSource? driveInfoSource = null)
    {
        replacer ??= new DriveAclReplacer(
            Mock.Of<IGrantSyncService>(),
            Mock.Of<ILoggingService>(),
            AclAccessorFactory.Create());
        sessionSaver ??= Mock.Of<IWizardSessionSaver>();
        quickAccessPinService ??= Mock.Of<IQuickAccessPinService>();
        driveInfoSource ??= Mock.Of<IPrepareSystemDriveInfoSource>();

        return new PrepareSystemTemplate(
            replacer,
            sessionSaver,
            new SessionContext
{
                Database = new AppDatabase(),
                CredentialStore = new CredentialStore(),
            }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32)),
            quickAccessPinService,
            driveInfoSource,
            Mock.Of<ILoggingService>());
    }

    private sealed class RecordingProgressReporter : IWizardProgressReporter
    {
        public List<string> Errors { get; } = [];
        public List<string> Warnings { get; } = [];
        public CancellationToken CancellationToken => CancellationToken.None;

        public void ReportStatus(string message)
        {
        }

        public void ReportWarning(string message) => Warnings.Add(message);

        public void ReportError(string message) => Errors.Add(message);
    }
}
