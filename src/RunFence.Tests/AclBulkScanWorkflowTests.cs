using Moq;
using RunFence.Account;
using RunFence.Acl;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class AclBulkScanWorkflowTests
{
    [Fact]
    public async Task RunAsync_CompletesSharedWorkflowAndShowsSkippedWarning()
    {
        var owner = Mock.Of<IWin32Window>();
        var rootPath = @"C:\scan-root";
        var sid = "S-1-5-21-1-2-3-1001";
        var scanResults = new Dictionary<string, AccountScanResult>(StringComparer.OrdinalIgnoreCase)
        {
            [sid] = new([new DiscoveredGrant(@"C:\data", false, false, false, true, false, false)], [])
        };
        var selectedResults = new Dictionary<string, AccountScanResult>(StringComparer.OrdinalIgnoreCase)
        {
            [sid] = scanResults[sid]
        };
        var summary = new AclBulkScanImportSummary(1, 0, [@"C:\conflict"]);

        var folderDialog = new Mock<IFolderBrowserDialogAdapter>();
        folderDialog.SetupGet(dialog => dialog.Dialog).Returns(new FolderBrowserDialog
        {
            SelectedPath = rootPath
        });
        folderDialog.Setup(dialog => dialog.ShowDialog(owner)).Returns(DialogResult.OK);

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
        processor.Setup(service => service.ApplyScanResults(selectedResults, It.IsAny<Action>()))
            .Callback<Dictionary<string, AccountScanResult>, Action>((_, save) => save())
            .Returns(summary);

        var resultDialog = new Mock<IAclBulkScanResultDialog>();
        resultDialog.SetupGet(dialog => dialog.Form).Returns(new Form());
        resultDialog.SetupGet(dialog => dialog.SelectedResults).Returns(selectedResults);

        var resultDialogFactory = new Mock<IAclBulkScanResultDialogFactory>();
        resultDialogFactory.Setup(factory => factory.Create(scanResults, It.IsAny<ISidNameCacheService>()))
            .Returns(resultDialog.Object);

        var warningPresenter = new Mock<IAclBulkScanWarningPresenter>();
        var workflow = new AclBulkScanWorkflow(
            bulkScan.Object,
            Mock.Of<ILoggingService>(),
            Mock.Of<ISidNameCacheService>(),
            processor.Object,
            warningPresenter.Object,
            resultDialogFactory.Object,
            folderDialogFactory.Object);

        var enabledStates = new List<bool>();
        var statuses = new List<string>();
        var shownDialogs = 0;
        var saveCalls = 0;

        await workflow.RunAsync(new TestWorkflowContext
        {
            Owner = owner,
            KnownSids = new HashSet<string>([sid], StringComparer.OrdinalIgnoreCase),
            SetScanBusyCore = busy => enabledStates.Add(!busy),
            SetStatusTextCore = text => statuses.Add(text),
            ShowResultsCore = _ =>
            {
                shownDialogs++;
                return DialogResult.OK;
            },
            SaveImportedResultsCore = () => saveCalls++
        });

        folderDialog.Verify(dialog => dialog.ShowDialog(owner), Times.Once);
        bulkScan.Verify(service => service.ScanAllAccountsAsync(
            rootPath,
            It.Is<IReadOnlySet<string>>(sids => sids.Count == 1 && sids.Contains(sid)),
            It.IsAny<IProgress<long>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        processor.Verify(service => service.ApplyScanResults(selectedResults, It.IsAny<Action>()), Times.Once);
        warningPresenter.Verify(service => service.ShowSkippedConflictWarning(summary, "Scan ACLs"), Times.Once);
        Assert.Equal([false, true], enabledStates);
        Assert.Contains("Scanning ACLs...", statuses);
        Assert.Equal("Ready", statuses[^1]);
        Assert.Equal(1, shownDialogs);
        Assert.Equal(1, saveCalls);
    }

    [Fact]
    public async Task RunAsync_WhenNoKnownSids_ShowsContextMessageAndSkipsScan()
    {
        var folderDialog = new Mock<IFolderBrowserDialogAdapter>();
        folderDialog.SetupGet(dialog => dialog.Dialog).Returns(new FolderBrowserDialog
        {
            SelectedPath = @"C:\scan-root"
        });
        folderDialog.Setup(dialog => dialog.ShowDialog(It.IsAny<IWin32Window?>())).Returns(DialogResult.OK);

        var folderDialogFactory = new Mock<IFolderBrowserDialogAdapterFactory>();
        folderDialogFactory.Setup(factory => factory.Create()).Returns(folderDialog.Object);

        var bulkScan = new Mock<IAccountAclBulkScanService>(MockBehavior.Strict);
        var processor = new Mock<IAclBulkScanResultProcessor>(MockBehavior.Strict);
        var warningPresenter = new Mock<IAclBulkScanWarningPresenter>(MockBehavior.Strict);

        var workflow = new AclBulkScanWorkflow(
            bulkScan.Object,
            Mock.Of<ILoggingService>(),
            Mock.Of<ISidNameCacheService>(),
            processor.Object,
            warningPresenter.Object,
            Mock.Of<IAclBulkScanResultDialogFactory>(),
            folderDialogFactory.Object);

        var context = new TestWorkflowContext
        {
            KnownSids = []
        };

        await workflow.RunAsync(context);

        Assert.Equal(1, context.NoKnownSidsShown);
        Assert.Equal(0, context.NoResultsShown);
        Assert.Equal(0, context.ScanFailuresShown);
        Assert.Equal(0, context.ResultsShown);
    }

    [Fact]
    public async Task RunAsync_WhenScanFails_LogsAndShowsContextFailure()
    {
        var owner = Mock.Of<IWin32Window>();
        var rootPath = @"C:\scan-root";
        var exception = new InvalidOperationException("boom");
        var folderDialog = new Mock<IFolderBrowserDialogAdapter>();
        folderDialog.SetupGet(dialog => dialog.Dialog).Returns(new FolderBrowserDialog
        {
            SelectedPath = rootPath
        });
        folderDialog.Setup(dialog => dialog.ShowDialog(owner)).Returns(DialogResult.OK);

        var folderDialogFactory = new Mock<IFolderBrowserDialogAdapterFactory>();
        folderDialogFactory.Setup(factory => factory.Create()).Returns(folderDialog.Object);

        var bulkScan = new Mock<IAccountAclBulkScanService>();
        bulkScan.Setup(service => service.ScanAllAccountsAsync(
                rootPath,
                It.IsAny<IReadOnlySet<string>>(),
                It.IsAny<IProgress<long>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var log = new Mock<ILoggingService>();
        var workflow = new AclBulkScanWorkflow(
            bulkScan.Object,
            log.Object,
            Mock.Of<ISidNameCacheService>(),
            Mock.Of<IAclBulkScanResultProcessor>(),
            Mock.Of<IAclBulkScanWarningPresenter>(),
            Mock.Of<IAclBulkScanResultDialogFactory>(),
            folderDialogFactory.Object);

        var context = new TestWorkflowContext
        {
            Owner = owner,
            KnownSids = new HashSet<string>(["S-1-5-21-1"], StringComparer.OrdinalIgnoreCase)
        };

        await workflow.RunAsync(context);

        Assert.Equal(1, context.ScanFailuresShown);
        Assert.Same(exception, context.LastFailure);
        Assert.Equal(0, context.ResultsShown);
        Assert.Equal("Ready", context.Statuses[^1]);
        Assert.Equal([true, false], context.BusyStates);
    }

    [Fact]
    public async Task RunAsync_WhenScanResultsAreEmpty_ShowsContextNoResults()
    {
        var rootPath = @"C:\scan-root";
        var sid = "S-1-5-21-1-2-3-1001";
        var scanResults = new Dictionary<string, AccountScanResult>(StringComparer.OrdinalIgnoreCase);

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
                It.IsAny<IReadOnlySet<string>>(),
                It.IsAny<IProgress<long>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(scanResults);

        var workflow = new AclBulkScanWorkflow(
            bulkScan.Object,
            Mock.Of<ILoggingService>(),
            Mock.Of<ISidNameCacheService>(),
            new Mock<IAclBulkScanResultProcessor>(MockBehavior.Strict).Object,
            Mock.Of<IAclBulkScanWarningPresenter>(),
            Mock.Of<IAclBulkScanResultDialogFactory>(),
            folderDialogFactory.Object);

        var context = new TestWorkflowContext
        {
            KnownSids = new HashSet<string>([sid], StringComparer.OrdinalIgnoreCase)
        };

        await workflow.RunAsync(context);

        Assert.Equal(1, context.NoResultsShown);
        Assert.Equal(0, context.ResultsShown);
    }

    private sealed class TestWorkflowContext : IAclBulkScanWorkflowContext
    {
        public IWin32Window? Owner { get; init; }
        public HashSet<string> KnownSids { get; init; } = [];
        public List<bool> BusyStates { get; } = [];
        public List<string> Statuses { get; } = [];
        public int ResultsShown { get; private set; }
        public int NoKnownSidsShown { get; private set; }
        public int NoResultsShown { get; private set; }
        public int ScanFailuresShown { get; private set; }
        public Exception? LastFailure { get; private set; }
        public Action<bool>? SetScanBusyCore { get; init; }
        public Action<string>? SetStatusTextCore { get; init; }
        public Func<Form, DialogResult>? ShowResultsCore { get; init; }
        public Action? SaveImportedResultsCore { get; init; }

        public string FailureLogMessage => "ACL bulk scan failed";

        public Task<HashSet<string>> GetKnownSidsAsync() => Task.FromResult(KnownSids);

        public void SetScanBusy(bool busy)
        {
            BusyStates.Add(busy);
            SetScanBusyCore?.Invoke(busy);
        }

        public void SetStatusText(string text)
        {
            Statuses.Add(text);
            SetStatusTextCore?.Invoke(text);
        }

        public DialogResult ShowResults(Form dialog)
        {
            ResultsShown++;
            return ShowResultsCore?.Invoke(dialog) ?? DialogResult.Cancel;
        }

        public void SaveImportedResults() => SaveImportedResultsCore?.Invoke();

        public void ShowNoKnownSids() => NoKnownSidsShown++;

        public void ShowNoResults() => NoResultsShown++;

        public void ShowScanFailed(Exception exception)
        {
            ScanFailuresShown++;
            LastFailure = exception;
        }
    }
}
