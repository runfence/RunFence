using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Acl;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class AccountBulkScanHandlerTests
{
    [Fact]
    public async Task ScanAcls_RunsSharedWorkflowAndSavesSelectedResults()
    {
        var sid = "S-1-5-21-1-2-3-1001";
        var rootPath = @"C:\scan-root";
        var scanResults = new Dictionary<string, AccountScanResult>(StringComparer.OrdinalIgnoreCase)
        {
            [sid] = new([new DiscoveredGrant(@"C:\data", false, false, false, true, false, false)], [])
        };
        var filteredResults = new Dictionary<string, AccountScanResult>(StringComparer.OrdinalIgnoreCase)
        {
            [sid] = scanResults[sid]
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

        var bulkScan = new Mock<IAccountAclBulkScanService>();
        bulkScan.Setup(service => service.ScanAllAccountsAsync(
                rootPath,
                It.Is<IReadOnlySet<string>>(sids => sids.Count == 1 && sids.Contains(sid)),
                It.IsAny<IProgress<long>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(scanResults);

        var processor = new Mock<IAclBulkScanResultProcessor>();
        processor.Setup(service => service.FilterManagedPaths(
                scanResults,
                It.IsAny<IReadOnlyList<AppEntry>>(),
                It.IsAny<IAclService>()))
            .Returns(filteredResults);
        processor.Setup(service => service.ApplyScanResults(filteredResults, It.IsAny<Action>()))
            .Callback<Dictionary<string, AccountScanResult>, Action>((_, saveDatabase) => saveDatabase())
            .Returns(summary);

        var resultDialog = new Mock<IAclBulkScanResultDialog>();
        resultDialog.SetupGet(dialog => dialog.Form).Returns(new Form());
        resultDialog.SetupGet(dialog => dialog.SelectedResults).Returns(filteredResults);

        var resultDialogFactory = new Mock<IAclBulkScanResultDialogFactory>();
        resultDialogFactory.Setup(factory => factory.Create(filteredResults, It.IsAny<ISidNameCacheService>()))
            .Returns(resultDialog.Object);

        var warningPresenter = new Mock<IAclBulkScanWarningPresenter>();
        var messagePresenter = new Mock<IAclBulkScanMessagePresenter>();
        var workflow = new AclBulkScanWorkflow(
            bulkScan.Object,
            Mock.Of<IAclService>(),
            Mock.Of<ILoggingService>(),
            Mock.Of<ISidNameCacheService>(),
            processor.Object,
            warningPresenter.Object,
            resultDialogFactory.Object,
            folderDialogFactory.Object,
            new LambdaDatabaseProvider(() => new AppDatabase()));

        var owner = new Control();
        using var form = new Form();
        form.Controls.Add(owner);

        var context = new Mock<IAccountsPanelContext>();
        context.SetupGet(c => c.OwnerControl).Returns(owner);
        context.SetupGet(c => c.CredentialStore).Returns(new CredentialStore
        {
            Credentials =
            [
                new CredentialEntry { Sid = sid },
                new CredentialEntry { Sid = string.Empty }
            ]
        });
        context.Setup(c => c.ShowModal(It.IsAny<Form>())).Returns(DialogResult.OK);

        var progress = new Mock<IScanProgressReporter>();
        var handler = new AccountBulkScanHandler(workflow, messagePresenter.Object);

        await handler.ScanAcls(context.Object, progress.Object);

        folderDialog.Verify(dialog => dialog.ShowDialog(form), Times.Once);
        bulkScan.Verify(service => service.ScanAllAccountsAsync(
            rootPath,
            It.Is<IReadOnlySet<string>>(sids => sids.Count == 1 && sids.Contains(sid)),
            It.IsAny<IProgress<long>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        processor.Verify(service => service.FilterManagedPaths(scanResults, It.IsAny<IReadOnlyList<AppEntry>>(), It.IsAny<IAclService>()), Times.Once);
        processor.Verify(service => service.ApplyScanResults(filteredResults, It.IsAny<Action>()), Times.Once);
        context.Verify(c => c.ShowModal(It.IsAny<Form>()), Times.Once);
        context.Verify(c => c.SaveAndRefresh(null, -1), Times.Once);
        warningPresenter.Verify(service => service.ShowSkippedConflictWarning(summary, "Scan ACLs"), Times.Once);
        progress.Verify(p => p.SetScanEnabled(false), Times.Once);
        progress.Verify(p => p.SetScanEnabled(true), Times.Once);
        progress.Verify(p => p.SetStatus("Scanning ACLs..."), Times.Once);
        progress.Verify(p => p.SetStatus("Ready"), Times.Once);
    }

    [Fact]
    public async Task ScanAcls_WhenNoKnownAccounts_PresentsExistingMessage()
    {
        var folderDialog = new Mock<IFolderBrowserDialogAdapter>();
        folderDialog.SetupGet(dialog => dialog.Dialog).Returns(new FolderBrowserDialog
        {
            SelectedPath = @"C:\scan-root"
        });
        folderDialog.Setup(dialog => dialog.ShowDialog(It.IsAny<IWin32Window?>())).Returns(DialogResult.OK);

        var folderDialogFactory = new Mock<IFolderBrowserDialogAdapterFactory>();
        folderDialogFactory.Setup(factory => factory.Create()).Returns(folderDialog.Object);

        var workflow = new AclBulkScanWorkflow(
            new Mock<IAccountAclBulkScanService>(MockBehavior.Strict).Object,
            Mock.Of<IAclService>(),
            Mock.Of<ILoggingService>(),
            Mock.Of<ISidNameCacheService>(),
            new Mock<IAclBulkScanResultProcessor>(MockBehavior.Strict).Object,
            new Mock<IAclBulkScanWarningPresenter>(MockBehavior.Strict).Object,
            Mock.Of<IAclBulkScanResultDialogFactory>(),
            folderDialogFactory.Object,
            new LambdaDatabaseProvider(() => new AppDatabase()));

        var owner = new Control();
        using var form = new Form();
        form.Controls.Add(owner);

        var context = new Mock<IAccountsPanelContext>();
        context.SetupGet(c => c.OwnerControl).Returns(owner);
        context.SetupGet(c => c.CredentialStore).Returns(new CredentialStore
        {
            Credentials = [new CredentialEntry { Sid = string.Empty }]
        });

        var progress = new Mock<IScanProgressReporter>(MockBehavior.Strict);
        var messagePresenter = new Mock<IAclBulkScanMessagePresenter>();
        var handler = new AccountBulkScanHandler(workflow, messagePresenter.Object);

        await handler.ScanAcls(context.Object, progress.Object);

        messagePresenter.Verify(
            presenter => presenter.ShowNoKnownSids(form, "No known accounts to scan for."),
            Times.Once);
        messagePresenter.Verify(
            presenter => presenter.ShowNoResults(It.IsAny<IWin32Window?>(), It.IsAny<string>()),
            Times.Never);
        messagePresenter.Verify(
            presenter => presenter.ShowScanFailed(It.IsAny<IWin32Window?>(), It.IsAny<Exception>()),
            Times.Never);
        context.Verify(c => c.ShowModal(It.IsAny<Form>()), Times.Never);
    }

    [Fact]
    public async Task ScanAcls_WhenScanFails_PresentsExistingFailureMessage()
    {
        var sid = "S-1-5-21-1-2-3-1001";
        var exception = new InvalidOperationException("boom");
        var folderDialog = new Mock<IFolderBrowserDialogAdapter>();
        folderDialog.SetupGet(dialog => dialog.Dialog).Returns(new FolderBrowserDialog
        {
            SelectedPath = @"C:\scan-root"
        });
        folderDialog.Setup(dialog => dialog.ShowDialog(It.IsAny<IWin32Window?>())).Returns(DialogResult.OK);

        var folderDialogFactory = new Mock<IFolderBrowserDialogAdapterFactory>();
        folderDialogFactory.Setup(factory => factory.Create()).Returns(folderDialog.Object);

        var bulkScan = new Mock<IAccountAclBulkScanService>();
        bulkScan.Setup(service => service.ScanAllAccountsAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlySet<string>>(),
                It.IsAny<IProgress<long>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var workflow = new AclBulkScanWorkflow(
            bulkScan.Object,
            Mock.Of<IAclService>(),
            Mock.Of<ILoggingService>(),
            Mock.Of<ISidNameCacheService>(),
            new Mock<IAclBulkScanResultProcessor>(MockBehavior.Strict).Object,
            new Mock<IAclBulkScanWarningPresenter>(MockBehavior.Strict).Object,
            Mock.Of<IAclBulkScanResultDialogFactory>(),
            folderDialogFactory.Object,
            new LambdaDatabaseProvider(() => new AppDatabase()));

        var owner = new Control();
        using var form = new Form();
        form.Controls.Add(owner);

        var context = new Mock<IAccountsPanelContext>();
        context.SetupGet(c => c.OwnerControl).Returns(owner);
        context.SetupGet(c => c.CredentialStore).Returns(new CredentialStore
        {
            Credentials = [new CredentialEntry { Sid = sid }]
        });

        var progress = new Mock<IScanProgressReporter>();
        var messagePresenter = new Mock<IAclBulkScanMessagePresenter>();
        var handler = new AccountBulkScanHandler(workflow, messagePresenter.Object);

        await handler.ScanAcls(context.Object, progress.Object);

        messagePresenter.Verify(
            presenter => presenter.ShowScanFailed(form, exception),
            Times.Once);
        messagePresenter.Verify(
            presenter => presenter.ShowNoKnownSids(It.IsAny<IWin32Window?>(), It.IsAny<string>()),
            Times.Never);
        messagePresenter.Verify(
            presenter => presenter.ShowNoResults(It.IsAny<IWin32Window?>(), It.IsAny<string>()),
            Times.Never);
        context.Verify(c => c.ShowModal(It.IsAny<Form>()), Times.Never);
    }

    [Fact]
    public async Task ScanAcls_WhenNoResults_PresentsExistingNoResultsMessage()
    {
        var sid = "S-1-5-21-1-2-3-1001";
        var scanResults = new Dictionary<string, AccountScanResult>(StringComparer.OrdinalIgnoreCase)
        {
            [sid] = new([new DiscoveredGrant(@"C:\data", false, false, false, true, false, false)], [])
        };

        var folderDialog = new Mock<IFolderBrowserDialogAdapter>();
        folderDialog.SetupGet(dialog => dialog.Dialog).Returns(new FolderBrowserDialog
        {
            SelectedPath = @"C:\scan-root"
        });
        folderDialog.Setup(dialog => dialog.ShowDialog(It.IsAny<IWin32Window?>())).Returns(DialogResult.OK);

        var folderDialogFactory = new Mock<IFolderBrowserDialogAdapterFactory>();
        folderDialogFactory.Setup(factory => factory.Create()).Returns(folderDialog.Object);

        var bulkScan = new Mock<IAccountAclBulkScanService>();
        bulkScan.Setup(service => service.ScanAllAccountsAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlySet<string>>(),
                It.IsAny<IProgress<long>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(scanResults);

        var processor = new Mock<IAclBulkScanResultProcessor>();
        processor.Setup(service => service.FilterManagedPaths(
                scanResults,
                It.IsAny<IReadOnlyList<AppEntry>>(),
                It.IsAny<IAclService>()))
            .Returns([]);

        var workflow = new AclBulkScanWorkflow(
            bulkScan.Object,
            Mock.Of<IAclService>(),
            Mock.Of<ILoggingService>(),
            Mock.Of<ISidNameCacheService>(),
            processor.Object,
            new Mock<IAclBulkScanWarningPresenter>(MockBehavior.Strict).Object,
            Mock.Of<IAclBulkScanResultDialogFactory>(),
            folderDialogFactory.Object,
            new LambdaDatabaseProvider(() => new AppDatabase()));

        var owner = new Control();
        using var form = new Form();
        form.Controls.Add(owner);

        var context = new Mock<IAccountsPanelContext>();
        context.SetupGet(c => c.OwnerControl).Returns(owner);
        context.SetupGet(c => c.CredentialStore).Returns(new CredentialStore
        {
            Credentials = [new CredentialEntry { Sid = sid }]
        });

        var progress = new Mock<IScanProgressReporter>();
        var messagePresenter = new Mock<IAclBulkScanMessagePresenter>();
        var handler = new AccountBulkScanHandler(workflow, messagePresenter.Object);

        await handler.ScanAcls(context.Object, progress.Object);

        messagePresenter.Verify(
            presenter => presenter.ShowNoResults(form, "No ACL entries found for the known accounts in the selected folder."),
            Times.Once);
        messagePresenter.Verify(
            presenter => presenter.ShowNoKnownSids(It.IsAny<IWin32Window?>(), It.IsAny<string>()),
            Times.Never);
        messagePresenter.Verify(
            presenter => presenter.ShowScanFailed(It.IsAny<IWin32Window?>(), It.IsAny<Exception>()),
            Times.Never);
        context.Verify(c => c.ShowModal(It.IsAny<Form>()), Times.Never);
    }
}
