using System.Windows.Forms;
using RunFence.Acl.Permissions;
using RunFence.Acl.UI;
using RunFence.Acl.UI.Forms;
using RunFence.Core.Models;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class AclManagerScanCancellationControllerTests
{
    [Fact]
    public void CancelActiveScan_WithActiveScan_CancelsReturnedToken()
    {
        var controller = new AclManagerScanCancellationController();

        var token = controller.BeginScan();
        controller.CancelActiveScan();

        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public void BeginScan_WhenScanAlreadyActive_Throws()
    {
        var controller = new AclManagerScanCancellationController();

        controller.BeginScan();

        var ex = Assert.Throws<InvalidOperationException>(() => controller.BeginScan());
        Assert.Contains("already active", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompleteScan_CleansUpActiveSource_AndAllowsReplacementScan()
    {
        var controller = new AclManagerScanCancellationController();

        var firstToken = controller.BeginScan();
        controller.CancelActiveScan();
        controller.CompleteScan();

        var secondToken = controller.BeginScan();

        Assert.True(firstToken.IsCancellationRequested);
        Assert.False(secondToken.IsCancellationRequested);

        controller.CompleteScan();
    }

    [Fact]
    public void AclManagerDialog_CloseWithoutPending_CancelsActiveScan()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var controller = new AclManagerScanCancellationController();
            var dialog = CreateDialog(controller);
            using (dialog)
            {
                var token = controller.BeginScan();
                var args = new FormClosingEventArgs(CloseReason.UserClosing, false);

                dialog.TriggerFormClosing(args);

                Assert.False(args.Cancel);
                Assert.True(token.IsCancellationRequested);
            }
        });
    }

    [Fact]
    public void AclManagerDialog_CloseDecision_AllowsCloseWithoutPending_CancelsActiveScanAndLeavesPendingEmpty()
    {
        var pending = new AclManagerPendingChanges();
        var controller = new AclManagerScanCancellationController();
        var closeCoordinator = new AclManagerCloseCoordinator(pending, controller);

        var token = controller.BeginScan();
        var args = new FormClosingEventArgs(CloseReason.UserClosing, false);

        closeCoordinator.ApplyCloseDecision(args, cancelClose: false);

        Assert.False(args.Cancel);
        Assert.True(token.IsCancellationRequested);
        Assert.False(pending.HasPendingChanges);
    }

    [Fact]
    public void AclManagerDialog_CloseDecision_AllowsCloseWithPending_CancelsActiveScanAndClearsPending()
    {
        var pending = new AclManagerPendingChanges();
        var controller = new AclManagerScanCancellationController();
        var closeCoordinator = new AclManagerCloseCoordinator(pending, controller);
        pending.Grants.AddGrant(new GrantedPathEntry { Path = @"C:\Pending", IsDeny = false });

        var token = controller.BeginScan();
        var args = new FormClosingEventArgs(CloseReason.UserClosing, false);

        closeCoordinator.ApplyCloseDecision(args, cancelClose: false);

        Assert.False(args.Cancel);
        Assert.True(token.IsCancellationRequested);
        Assert.False(pending.HasPendingChanges);
        Assert.Empty(pending.Grants.GetPendingAddsSnapshot());
    }

    [Fact]
    public void AclManagerDialog_CloseDecision_CancelsClose_LeavesScanAndPendingUnchanged()
    {
        var pending = new AclManagerPendingChanges();
        var controller = new AclManagerScanCancellationController();
        var closeCoordinator = new AclManagerCloseCoordinator(pending, controller);
        pending.Grants.AddGrant(new GrantedPathEntry { Path = @"C:\Pending", IsDeny = false });

        var token = controller.BeginScan();
        var args = new FormClosingEventArgs(CloseReason.UserClosing, false);

        closeCoordinator.ApplyCloseDecision(args, cancelClose: true);

        Assert.True(args.Cancel);
        Assert.False(token.IsCancellationRequested);
        Assert.True(pending.HasPendingChanges);
        Assert.Single(pending.Grants.GetPendingAddsSnapshot());
        Assert.Contains((@"C:\Pending", false), pending.Grants.GetPendingAddsSnapshot().Keys);
    }

    private static TestAclManagerDialog CreateDialog(AclManagerScanCancellationController controller)
    {
        var pending = new AclManagerPendingChanges();
        var selectionHandler = new AclManagerSelectionHandler(
            grantsHelper: null!,
            traverseHelper: null!,
            applyHandler: new AclManagerApplyOrchestrator(
                planBuilder: null!,
                applyExecutor: null!,
                postProcessor: null!,
                phaseCatalog: new AclApplyPhaseCatalog(),
                sessionSaver: null!,
                log: null!));
        selectionHandler.Initialize(
            isContainer: false,
            pending,
            controls: CreateDialogControls(),
            refreshTraverseGrid: static () => { });

        return new TestAclManagerDialog(
            aclPermission: null!,
            log: null!,
            traverseAutoManager: null!,
            grantsHelper: null!,
            traverseHelper: null!,
            dragDropHandler: null!,
            actionHandler: null!,
            applyHandler: null!,
            exportImport: null!,
            selectionHandler: selectionHandler,
            modificationHandler: null!,
            mouseEventHandler: null!,
            pathActionHelper: null!,
            applyPresenter: null!,
            sectionHeaderFactory: null!,
            scanCancellation: controller);
    }

    private static AclManagerDialogControls CreateDialogControls()
    {
        return new AclManagerDialogControls
        {
            TabControl = new TabControl(),
            TraverseTab = new TabPage(),
            GrantsGrid = new DataGridView(),
            TraverseGrid = new DataGridView(),
            AddFileButton = new ToolStripButton(),
            AddFolderButton = new ToolStripButton(),
            RemoveButton = new ToolStripButton(),
            FixAclsButton = new ToolStripButton(),
            ApplyButton = new Button(),
            ScanStatusLabel = new ToolStripLabel(),
            CtxAddFile = new ToolStripMenuItem(),
            CtxAddFolder = new ToolStripMenuItem(),
            CtxGrantsSep = new ToolStripSeparator(),
            CtxRemove = new ToolStripMenuItem(),
            CtxUntrack = new ToolStripMenuItem(),
            CtxFixAcls = new ToolStripMenuItem(),
            CtxGrantsOpenFolderSep = new ToolStripSeparator(),
            CtxOpenFolderGrants = new ToolStripMenuItem(),
            CtxCopyPathGrants = new ToolStripMenuItem(),
            CtxGrantsPropertiesSep = new ToolStripSeparator(),
            CtxPropertiesGrants = new ToolStripMenuItem(),
            CtxTraverseAddFile = new ToolStripMenuItem(),
            CtxTraverseAddFolder = new ToolStripMenuItem(),
            CtxTraverseSep = new ToolStripSeparator(),
            CtxTraverseRemove = new ToolStripMenuItem(),
            CtxTraverseUntrack = new ToolStripMenuItem(),
            CtxTraverseFixAcls = new ToolStripMenuItem(),
            CtxTraverseOpenFolderSep = new ToolStripSeparator(),
            CtxTraverseOpenFolder = new ToolStripMenuItem(),
            CtxTraverseCopyPath = new ToolStripMenuItem(),
            CtxTraversePropertiesSep = new ToolStripSeparator(),
            CtxTraverseProperties = new ToolStripMenuItem()
        };
    }

    private sealed class TestAclManagerDialog : AclManagerDialog
    {
        public TestAclManagerDialog(
            IAclPermissionService aclPermission,
            ILoggingService log,
            TraverseAutoManager traverseAutoManager,
            AclManagerGrantsHelper grantsHelper,
            AclManagerTraverseHelper traverseHelper,
            AclManagerDragDropHandler dragDropHandler,
            AclManagerActionOrchestrator actionHandler,
            AclManagerApplyOrchestrator applyHandler,
            AclManagerExportImport exportImport,
            AclManagerSelectionHandler selectionHandler,
            AclManagerModificationHandler modificationHandler,
            AclManagerMouseEventHandler mouseEventHandler,
            AclManagerPathActionHelper pathActionHelper,
            AclDialogApplyPresenter applyPresenter,
            AclManagerSectionHeaderFactory sectionHeaderFactory,
            AclManagerScanCancellationController scanCancellation)
            : base(
                aclPermission,
                log,
                traverseAutoManager,
                grantsHelper,
                traverseHelper,
                dragDropHandler,
                actionHandler,
                applyHandler,
                exportImport,
                selectionHandler,
                modificationHandler,
                mouseEventHandler,
                pathActionHelper,
                applyPresenter,
                sectionHeaderFactory,
                scanCancellation)
        {
        }

        public void TriggerFormClosing(FormClosingEventArgs args) => base.OnFormClosing(args);
    }
}
