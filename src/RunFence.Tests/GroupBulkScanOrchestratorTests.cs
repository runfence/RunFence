using Moq;
using RunFence.Account;
using RunFence.Acl;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Groups.UI;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class GroupBulkScanOrchestratorTests
{
    private const string GroupSid = "S-1-5-32-544";

    [Fact]
    public async Task ScanAcls_RunsSharedWorkflowAndSavesSelectedResults()
    {
        var rootPath = @"C:\scan-root";
        var scanResults = new Dictionary<string, AccountScanResult>(StringComparer.OrdinalIgnoreCase)
        {
            [GroupSid] = new([new DiscoveredGrant(@"C:\data", false, false, false, true, false, false)], [])
        };
        var summary = new AclBulkScanImportSummary(1, 0, [@"C:\conflict"]);

        var folderDialog = new Mock<IFolderBrowserDialogAdapter>();
        folderDialog.SetupGet(dialog => dialog.Dialog).Returns(new FolderBrowserDialog
        {
            SelectedPath = rootPath
        });
        folderDialog.Setup(dialog => dialog.ShowDialog(It.IsAny<IWin32Window?>())).Returns(DialogResult.OK);

        var folderDialogFactory = new Mock<IFolderBrowserDialogAdapterFactory>();
        folderDialogFactory.Setup(factory => factory.Create()).Returns(folderDialog.Object);

        var groupMembership = new Mock<ILocalGroupQueryService>();
        groupMembership.Setup(service => service.GetLocalGroups()).Returns(
        [
            new LocalUserAccount("Administrators", GroupSid),
            new LocalUserAccount("NoSid", string.Empty)
        ]);

        var bulkScan = new Mock<IAccountAclBulkScanService>();
        bulkScan.Setup(service => service.ScanAllAccountsAsync(
                rootPath,
                It.Is<IReadOnlySet<string>>(sids => sids.Count == 1 && sids.Contains(GroupSid)),
                It.IsAny<IProgress<long>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(scanResults);

        var processor = new Mock<IAclBulkScanResultProcessor>();
        processor.Setup(service => service.ApplyScanResults(scanResults, It.IsAny<Action>()))
            .Callback<Dictionary<string, AccountScanResult>, Action>((_, saveDatabase) => saveDatabase())
            .Returns(summary);

        var resultDialog = new Mock<IAclBulkScanResultDialog>();
        resultDialog.SetupGet(dialog => dialog.Form).Returns(new Form());
        resultDialog.SetupGet(dialog => dialog.SelectedResults).Returns(scanResults);

        var resultDialogFactory = new Mock<IAclBulkScanResultDialogFactory>();
        resultDialogFactory.Setup(factory => factory.Create(scanResults, It.IsAny<ISidNameCacheService>()))
            .Returns(resultDialog.Object);

        var warningPresenter = new Mock<IAclBulkScanWarningPresenter>();
        var messagePresenter = new Mock<IAclBulkScanMessagePresenter>();

        var modalCoordinator = new Mock<IModalCoordinator>();
        modalCoordinator.Setup(service => service.ShowModal(It.IsAny<Form>(), It.IsAny<IWin32Window?>()))
            .Returns(DialogResult.OK);

        var workflow = new AclBulkScanWorkflow(
            bulkScan.Object,
            Mock.Of<ILoggingService>(),
            Mock.Of<ISidNameCacheService>(),
            processor.Object,
            warningPresenter.Object,
            resultDialogFactory.Object,
            folderDialogFactory.Object);

        var sessionSaver = new Mock<ISessionSaver>();

        var orchestrator = new GroupBulkScanOrchestrator(
            modalCoordinator.Object,
            groupMembership.Object,
            workflow,
            messagePresenter.Object,
            sessionSaver.Object);
        var busyStates = new List<bool>();
        var statuses = new List<string>();
        var progressPresenter = new Mock<IGroupScanProgressPresenter>();
        progressPresenter.Setup(p => p.SetScanBusy(It.IsAny<bool>()))
            .Callback<bool>(busy => busyStates.Add(busy));
        progressPresenter.Setup(p => p.SetStatusText(It.IsAny<string>()))
            .Callback<string>(text => statuses.Add(text));
        var owner = Mock.Of<IWin32Window>();

        await orchestrator.ScanAcls(owner, progressPresenter.Object);

        folderDialog.Verify(dialog => dialog.ShowDialog(owner), Times.Once);
        bulkScan.Verify(service => service.ScanAllAccountsAsync(
            rootPath,
            It.Is<IReadOnlySet<string>>(sids => sids.Count == 1 && sids.Contains(GroupSid)),
            It.IsAny<IProgress<long>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        processor.Verify(service => service.ApplyScanResults(scanResults, It.IsAny<Action>()), Times.Once);
        modalCoordinator.Verify(service => service.ShowModal(It.IsAny<Form>(), owner), Times.Once);
        warningPresenter.Verify(service => service.ShowSkippedConflictWarning(summary, "Scan ACLs"), Times.Once);
        sessionSaver.Verify(s => s.SaveConfig(), Times.Once);
        Assert.Equal([true, false], busyStates);
        Assert.Contains("Scanning ACLs...", statuses);
        Assert.Equal("Ready", statuses[^1]);
    }

    [Fact]
    public async Task ScanAcls_WhenNoLocalGroups_PresentsExistingMessage()
    {
        var folderDialog = new Mock<IFolderBrowserDialogAdapter>();
        folderDialog.SetupGet(dialog => dialog.Dialog).Returns(new FolderBrowserDialog
        {
            SelectedPath = @"C:\scan-root"
        });
        folderDialog.Setup(dialog => dialog.ShowDialog(It.IsAny<IWin32Window?>())).Returns(DialogResult.OK);

        var folderDialogFactory = new Mock<IFolderBrowserDialogAdapterFactory>();
        folderDialogFactory.Setup(factory => factory.Create()).Returns(folderDialog.Object);

        var groupMembership = new Mock<ILocalGroupQueryService>();
        groupMembership.Setup(service => service.GetLocalGroups()).Returns(
        [
            new LocalUserAccount("NoSid", string.Empty)
        ]);

        var workflow = new AclBulkScanWorkflow(
            new Mock<IAccountAclBulkScanService>(MockBehavior.Strict).Object,
            Mock.Of<ILoggingService>(),
            Mock.Of<ISidNameCacheService>(),
            new Mock<IAclBulkScanResultProcessor>(MockBehavior.Strict).Object,
            new Mock<IAclBulkScanWarningPresenter>(MockBehavior.Strict).Object,
            Mock.Of<IAclBulkScanResultDialogFactory>(),
            folderDialogFactory.Object);

        var modalCoordinator = new Mock<IModalCoordinator>(MockBehavior.Strict);
        var messagePresenter = new Mock<IAclBulkScanMessagePresenter>();
        var orchestrator = new GroupBulkScanOrchestrator(
            modalCoordinator.Object,
            groupMembership.Object,
            workflow,
            messagePresenter.Object,
            Mock.Of<ISessionSaver>());

        var owner = Mock.Of<IWin32Window>();

        await orchestrator.ScanAcls(
            owner,
            Mock.Of<IGroupScanProgressPresenter>());

        messagePresenter.Verify(
            presenter => presenter.ShowNoKnownSids(owner, "No local groups to scan for."),
            Times.Once);
        messagePresenter.Verify(
            presenter => presenter.ShowNoResults(It.IsAny<IWin32Window?>(), It.IsAny<string>()),
            Times.Never);
        messagePresenter.Verify(
            presenter => presenter.ShowScanFailed(It.IsAny<IWin32Window?>(), It.IsAny<Exception>()),
            Times.Never);
        modalCoordinator.Verify(service => service.ShowModal(It.IsAny<Form>(), It.IsAny<IWin32Window?>()), Times.Never);
    }

    [Fact]
    public async Task ScanAcls_WhenScanFails_PresentsExistingFailureMessage()
    {
        var exception = new InvalidOperationException("boom");
        var folderDialog = new Mock<IFolderBrowserDialogAdapter>();
        folderDialog.SetupGet(dialog => dialog.Dialog).Returns(new FolderBrowserDialog
        {
            SelectedPath = @"C:\scan-root"
        });
        folderDialog.Setup(dialog => dialog.ShowDialog(It.IsAny<IWin32Window?>())).Returns(DialogResult.OK);

        var folderDialogFactory = new Mock<IFolderBrowserDialogAdapterFactory>();
        folderDialogFactory.Setup(factory => factory.Create()).Returns(folderDialog.Object);

        var groupMembership = new Mock<ILocalGroupQueryService>();
        groupMembership.Setup(service => service.GetLocalGroups()).Returns(
        [
            new LocalUserAccount("Administrators", GroupSid)
        ]);

        var bulkScan = new Mock<IAccountAclBulkScanService>();
        bulkScan.Setup(service => service.ScanAllAccountsAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlySet<string>>(),
                It.IsAny<IProgress<long>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var workflow = new AclBulkScanWorkflow(
            bulkScan.Object,
            Mock.Of<ILoggingService>(),
            Mock.Of<ISidNameCacheService>(),
            new Mock<IAclBulkScanResultProcessor>(MockBehavior.Strict).Object,
            new Mock<IAclBulkScanWarningPresenter>(MockBehavior.Strict).Object,
            Mock.Of<IAclBulkScanResultDialogFactory>(),
            folderDialogFactory.Object);

        var modalCoordinator = new Mock<IModalCoordinator>(MockBehavior.Strict);
        var messagePresenter = new Mock<IAclBulkScanMessagePresenter>();
        var orchestrator = new GroupBulkScanOrchestrator(
            modalCoordinator.Object,
            groupMembership.Object,
            workflow,
            messagePresenter.Object,
            Mock.Of<ISessionSaver>());

        var owner = Mock.Of<IWin32Window>();

        await orchestrator.ScanAcls(
            owner,
            Mock.Of<IGroupScanProgressPresenter>());

        messagePresenter.Verify(
            presenter => presenter.ShowScanFailed(owner, exception),
            Times.Once);
        messagePresenter.Verify(
            presenter => presenter.ShowNoKnownSids(It.IsAny<IWin32Window?>(), It.IsAny<string>()),
            Times.Never);
        messagePresenter.Verify(
            presenter => presenter.ShowNoResults(It.IsAny<IWin32Window?>(), It.IsAny<string>()),
            Times.Never);
        modalCoordinator.Verify(service => service.ShowModal(It.IsAny<Form>(), It.IsAny<IWin32Window?>()), Times.Never);
    }

    [Fact]
    public async Task ScanAcls_WhenNoResults_PresentsExistingNoResultsMessage()
    {
        var scanResults = new Dictionary<string, AccountScanResult>(StringComparer.OrdinalIgnoreCase);

        var folderDialog = new Mock<IFolderBrowserDialogAdapter>();
        folderDialog.SetupGet(dialog => dialog.Dialog).Returns(new FolderBrowserDialog
        {
            SelectedPath = @"C:\scan-root"
        });
        folderDialog.Setup(dialog => dialog.ShowDialog(It.IsAny<IWin32Window?>())).Returns(DialogResult.OK);

        var folderDialogFactory = new Mock<IFolderBrowserDialogAdapterFactory>();
        folderDialogFactory.Setup(factory => factory.Create()).Returns(folderDialog.Object);

        var groupMembership = new Mock<ILocalGroupQueryService>();
        groupMembership.Setup(service => service.GetLocalGroups()).Returns(
        [
            new LocalUserAccount("Administrators", GroupSid)
        ]);

        var bulkScan = new Mock<IAccountAclBulkScanService>();
        bulkScan.Setup(service => service.ScanAllAccountsAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlySet<string>>(),
                It.IsAny<IProgress<long>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(scanResults);

        var workflow = new AclBulkScanWorkflow(
            bulkScan.Object,
            Mock.Of<ILoggingService>(),
            Mock.Of<ISidNameCacheService>(),
            new Mock<IAclBulkScanResultProcessor>(MockBehavior.Strict).Object,
            new Mock<IAclBulkScanWarningPresenter>(MockBehavior.Strict).Object,
            Mock.Of<IAclBulkScanResultDialogFactory>(),
            folderDialogFactory.Object);

        var modalCoordinator = new Mock<IModalCoordinator>(MockBehavior.Strict);
        var messagePresenter = new Mock<IAclBulkScanMessagePresenter>();
        var orchestrator = new GroupBulkScanOrchestrator(
            modalCoordinator.Object,
            groupMembership.Object,
            workflow,
            messagePresenter.Object,
            Mock.Of<ISessionSaver>());

        var owner = Mock.Of<IWin32Window>();

        await orchestrator.ScanAcls(
            owner,
            Mock.Of<IGroupScanProgressPresenter>());

        messagePresenter.Verify(
            presenter => presenter.ShowNoResults(owner, "No ACL entries found for the local groups in the selected folder."),
            Times.Once);
        messagePresenter.Verify(
            presenter => presenter.ShowNoKnownSids(It.IsAny<IWin32Window?>(), It.IsAny<string>()),
            Times.Never);
        messagePresenter.Verify(
            presenter => presenter.ShowScanFailed(It.IsAny<IWin32Window?>(), It.IsAny<Exception>()),
            Times.Never);
        modalCoordinator.Verify(service => service.ShowModal(It.IsAny<Form>(), It.IsAny<IWin32Window?>()), Times.Never);
    }
}
