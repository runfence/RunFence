using System.ComponentModel;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.UI;
using RunFence.UI.Forms;

namespace RunFence.Acl.UI.Forms;

/// <summary>
/// ACL Manager dialog — manages per-path allow/deny grants and traverse paths for
/// a single account or AppContainer SID.
/// </summary>
/// <remarks>Deps above threshold: 13 deps: all handlers mutate shared <see cref="AclManagerPendingChanges"/> for atomic apply. Splitting would require passing the pending state between classes, breaking the current atomic transaction guarantee. Reviewed 2026-04-09.</remarks>
public partial class AclManagerDialog : RunFence.UI.Forms.ContextHelpForm, IAclManagerGridRefresher, IAclManagerDialogHost
{
    private readonly ILoggingService _log;
    private readonly IAclPermissionService _aclPermission;
    private readonly TraverseAutoManager _traverseAutoManager;
    private readonly AclManagerGrantsHelper _grantsHelper;
    private readonly AclManagerTraverseHelper _traverseHelper;
    private readonly AclManagerDragDropHandler _dragDropHandler;
    private readonly AclManagerActionOrchestrator _actionHandler;
    private readonly AclManagerApplyOrchestrator _applyHandler;
    private readonly AclManagerExportImport _exportImport;
    private readonly AclManagerSelectionHandler _selectionHandler;
    private readonly AclManagerModificationHandler _modificationHandler;
    private readonly AclManagerMouseEventHandler _mouseEventHandler;
    private readonly AclManagerPathActionHelper _pathActionHelper;
    private readonly AclDialogApplyPresenter _applyPresenter;
    private readonly AclManagerSectionHeaderFactory _sectionHeaderFactory;
    private readonly AclManagerScanCancellationController _scanCancellation;
    private readonly AclManagerCloseCoordinator _closeCoordinator;
    private DropFilesInterceptor? _grantsDropInterceptor;
    private DropFilesInterceptor? _traverseDropInterceptor;
    private readonly AclManagerPendingChanges _pending = new();
    private readonly GridSortHelper _grantsSortHelper = new();
    private readonly GridSortHelper _traverseSortHelper = new();
    private bool _isContainer;
    private bool _blocksOwnerColumn;

    public AclManagerDialog(
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
    {
        _aclPermission = aclPermission;
        _log = log;
        _traverseAutoManager = traverseAutoManager;
        _grantsHelper = grantsHelper;
        _traverseHelper = traverseHelper;
        _dragDropHandler = dragDropHandler;
        _actionHandler = actionHandler;
        _applyHandler = applyHandler;
        _exportImport = exportImport;
        _selectionHandler = selectionHandler;
        _modificationHandler = modificationHandler;
        _mouseEventHandler = mouseEventHandler;
        _pathActionHelper = pathActionHelper;
        _applyPresenter = applyPresenter;
        _sectionHeaderFactory = sectionHeaderFactory;
        _scanCancellation = scanCancellation;
        _closeCoordinator = new AclManagerCloseCoordinator(_pending, _scanCancellation);
    }

    public void Initialize(
        string sid,
        bool isContainer,
        string displayName)
    {
        _isContainer = isContainer;
        _blocksOwnerColumn = !AclHelper.CanAssignGrantOwner(sid, isContainer);

        InitializeComponent();

        DataGridViewGroupHeaderHelper.SuppressGroupHeaderTooltips<ConfigSectionHeader>(_grantsGrid);
        DataGridViewGroupHeaderHelper.SuppressGroupHeaderTooltips<ConfigSectionHeader>(_traverseGrid);

        var groupSids = _aclPermission.ResolveAccountGroupSids(sid);

        _traverseAutoManager.Initialize(_pending, sid, groupSids);

        _grantsHelper.Initialize(
            _grantsGrid, sid, isContainer, groupSids,
            _pending, _sectionHeaderFactory, _grantsSortHelper);

        _traverseHelper.Initialize(
            _traverseGrid, sid, isContainer, _pending, groupSids, _sectionHeaderFactory, _traverseSortHelper);

        _dragDropHandler.Initialize(sid, _pending);

        _actionHandler.Initialize(sid, isContainer, this, _pending, this);

        _applyHandler.Initialize(_pending, sid, isContainer);
        _exportImport.Initialize(_pending, sid, isContainer, this, ExecuteApplyAsync);

        var controls = BuildControls();

        _mouseEventHandler.Initialize(controls, RefreshGrantsGrid, RefreshTraverseGrid, _selectionHandler.UpdateActionButtons);
        _pathActionHelper.Initialize(this, controls);
        _selectionHandler.Initialize(isContainer, _pending, controls, RefreshTraverseGrid);
        _modificationHandler.Initialize(this, sid, _pending, _scanCancellation, controls,
            RefreshGrantsGrid, RefreshTraverseGrid, _selectionHandler.UpdateActionButtons);

        _selectionHandler.RemoveKeyPressed += (_, _) => _modificationHandler.RemoveSelected();

        BuildDynamicContent(displayName);
    }

    private AclManagerDialogControls BuildControls()
    {
        return new AclManagerDialogControls
        {
            TabControl = _tabControl,
            TraverseTab = _traverseTab,
            GrantsGrid = _grantsGrid,
            TraverseGrid = _traverseGrid,
            AddFileButton = _addFileButton,
            AddFolderButton = _addFolderButton,
            RemoveButton = _removeButton,
            FixAclsButton = _fixAclsButton,
            ApplyButton = _applyButton,
            ScanStatusLabel = _scanStatusLabel,
            CtxAddFile = _ctxAddFile,
            CtxAddFolder = _ctxAddFolder,
            CtxGrantsSep = _ctxGrantsSep,
            CtxRemove = _ctxRemove,
            CtxUntrack = _ctxUntrack,
            CtxFixAcls = _ctxFixAcls,
            CtxGrantsOpenFolderSep = _ctxGrantsOpenFolderSep,
            CtxOpenFolderGrants = _ctxOpenFolderGrants,
            CtxCopyPathGrants = _ctxCopyPathGrants,
            CtxGrantsPropertiesSep = _ctxGrantsPropertiesSep,
            CtxPropertiesGrants = _ctxPropertiesGrants,
            CtxTraverseAddFile = _ctxTraverseAddFile,
            CtxTraverseAddFolder = _ctxTraverseAddFolder,
            CtxTraverseSep = _ctxTraverseSep,
            CtxTraverseRemove = _ctxTraverseRemove,
            CtxTraverseUntrack = _ctxTraverseUntrack,
            CtxTraverseFixAcls = _ctxTraverseFixAcls,
            CtxTraverseOpenFolderSep = _ctxTraverseOpenFolderSep,
            CtxTraverseOpenFolder = _ctxTraverseOpenFolder,
            CtxTraverseCopyPath = _ctxTraverseCopyPath,
            CtxTraversePropertiesSep = _ctxTraversePropertiesSep,
            CtxTraverseProperties = _ctxTraverseProperties
        };
    }

    private int ButtonPadding => LogicalToDeviceUnits(8);

    private void BuildDynamicContent(string displayName)
    {
        Text = $"ACL Manager \u2014 {displayName}";
        _formIcon = UiIconFactory.CreateDialogIcon("\U0001F4DC", Color.FromArgb(0xCC, 0x99, 0x00));
        Icon = _formIcon;

        _ctxPropertiesGrants.Image = UiIconFactory.CreatePropertiesIcon();
        _ctxTraverseProperties.Image = UiIconFactory.CreatePropertiesIcon();

        // Position Close and Apply buttons
        _closeButton.Location = new Point(ClientSize.Width - _closeButton.Width - ButtonPadding,
            ClientSize.Height - _closeButton.Height - ButtonPadding);
        _applyButton.Location = new Point(_closeButton.Left - _applyButton.Width - ButtonPadding,
            ClientSize.Height - _applyButton.Height - ButtonPadding);

        AclManagerDialogGridSetup.BuildGrantsGrid(_grantsGrid, _isContainer);
        AclManagerDialogGridSetup.BuildTraverseGrid(_traverseGrid);
        if (_isContainer)
            _tabControl.TabPages.Remove(_traverseTab);

        // Suppress ComboBox cell formatting errors
        _grantsGrid.DataError += (_, e) => { e.ThrowException = false; };
        _grantsGrid.CellPainting += OnGrantsCellPainting;

        AclManagerDialogUiSetup.ConfigureToolbar(_toolStrip, _addFileButton, _addFolderButton, _scanButton, _removeButton, _fixAclsButton, _exportButton, _importButton);
        _toolStrip.PerformLayout();
        PositionTabControl();

        // Enable three-state column sorting with section-header awareness
        _grantsSortHelper.EnableThreeStateSorting(_grantsGrid, RefreshGrantsGrid, sectioned: true);
        _traverseSortHelper.EnableThreeStateSorting(_traverseGrid, RefreshTraverseGrid, sectioned: true);

        RefreshGrantsGrid();
        RefreshTraverseGrid();

        _grantsGrid.HandleCreated += (_, _) =>
        {
            var old = _grantsDropInterceptor;
            _grantsDropInterceptor = new DropFilesInterceptor(_grantsGrid.Handle, _mouseEventHandler.HandleGrantsFileDrop);
            old?.Dispose();
        };
        _traverseGrid.HandleCreated += (_, _) =>
        {
            var old = _traverseDropInterceptor;
            _traverseDropInterceptor = new DropFilesInterceptor(_traverseGrid.Handle, _mouseEventHandler.HandleTraverseFileDrop);
            old?.Dispose();
        };
        Disposed += (_, _) =>
        {
            _grantsDropInterceptor?.Dispose();
            _traverseDropInterceptor?.Dispose();
        };

        Resize += OnResize;
        Shown += OnShown;
        RegisterContextHelp();
    }

    private void RegisterContextHelp()
    {
        SetContextHelp(_contextHelpButton, ContextHelpTextResolver.InstructionText);
        SetContextHelp(_grantsTab, ContextHelpTextCatalog.AclManager_Grants);
        if (!_isContainer)
            SetContextHelp(_traverseTab, ContextHelpTextCatalog.AclManager_Traverse);
    }

    private void OnShown(object? sender, EventArgs e)
    {
        _grantsGrid.ClearSelection();
        _traverseGrid.ClearSelection();
        _selectionHandler.UpdateActionButtons();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_selectionHandler.HandleFormClosing())
        {
            _closeCoordinator.ApplyCloseDecision(e, cancelClose: true);
            return;
        }

        _closeCoordinator.ApplyCloseDecision(e, cancelClose: false);

        base.OnFormClosing(e);
    }

    public void RefreshGrantsGrid()
    {
        _grantsHelper.PopulateGrantsGrid();
        _grantsSortHelper.ReapplyGlyphIfActive(_grantsGrid);
    }

    public void RefreshTraverseGrid()
    {
        _traverseHelper.PopulateTraverseGrid();
        _traverseSortHelper.ReapplyGlyphIfActive(_traverseGrid);
    }

    private void RefreshGrids()
    {
        RefreshGrantsGrid();
        RefreshTraverseGrid();
        _selectionHandler.UpdateActionButtons();
    }

    private void PositionTabControl()
    {
        int topRowHeight = _topRowHost.Height;
        _tabControl.Location = new Point(0, topRowHeight);
        _tabControl.Size = new Size(ClientSize.Width, ClientSize.Height - topRowHeight - _closeButton.Height - ButtonPadding * 2);
    }

    private void OnResize(object? sender, EventArgs e)
    {
        _closeButton.Location = new Point(ClientSize.Width - _closeButton.Width - ButtonPadding,
            ClientSize.Height - _closeButton.Height - ButtonPadding);
        _applyButton.Location = new Point(_closeButton.Left - _applyButton.Width - ButtonPadding,
            ClientSize.Height - _applyButton.Height - ButtonPadding);
        PositionTabControl();
    }

    // --- Named handlers for Designer.cs event wiring ---
    // These thin delegates are required because Designer.cs wires events by method name.

    private void OnTabChanged(object? sender, EventArgs e) => _selectionHandler.HandleTabChanged();
    private void OnGrantsSelectionChanged(object? sender, EventArgs e) => _selectionHandler.HandleSelectionChanged();
    private void OnTraverseSelectionChanged(object? sender, EventArgs e) => _selectionHandler.HandleSelectionChanged();
    private void OnGrantsCellValueChanged(object? sender, DataGridViewCellEventArgs e) => _selectionHandler.HandleGrantsCellValueChanged(e);
    private void OnGrantsCurrentCellDirtyStateChanged(object? sender, EventArgs e) => _selectionHandler.HandleGrantsDirtyStateChanged();
    private void OnGrantsCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (!_blocksOwnerColumn || e.RowIndex < 0 || e.ColumnIndex < 0)
            return;
        if (_grantsGrid.Columns[e.ColumnIndex].Name != AclManagerGrantsHelper.ColOwner)
            return;
        if (_grantsGrid.Rows[e.RowIndex].Tag is not GrantedPathEntry)
            return;

        e.PaintBackground(e.ClipBounds, true);
        var checkState = e.Value is RightCheckState.Checked or true or CheckState.Checked;
        var state = checkState ? ButtonState.Checked | ButtonState.Inactive : ButtonState.Inactive;
        var size = SystemInformation.MenuCheckSize;
        var rect = new Rectangle(
            e.CellBounds.X + (e.CellBounds.Width - size.Width) / 2,
            e.CellBounds.Y + (e.CellBounds.Height - size.Height) / 2,
            size.Width,
            size.Height);

        var graphics = e.Graphics;
        if (graphics == null)
            return;

        ControlPaint.DrawCheckBox(graphics, rect, state);
        e.Handled = true;
    }

    private async void OnScanFolderClick(object? sender, EventArgs e)
    {
        if (_pending.HasPendingChanges)
        {
            var result = MessageBox.Show(
                "You have unapplied changes. Apply them first?",
                "ACL Manager", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (result == DialogResult.Cancel)
                return;
            if (!await ExecuteApplyAsync())
                return;
        }
        _modificationHandler.ScanFolder();
    }

    private void OnGrantsContextMenuOpening(object? sender, CancelEventArgs e) => _selectionHandler.HandleGrantsContextMenuOpening();
    private void OnTraverseContextMenuOpening(object? sender, CancelEventArgs e) => _selectionHandler.HandleTraverseContextMenuOpening();
    private void OnGridKeyDown(object? sender, KeyEventArgs e) => _selectionHandler.HandleGridKeyDown(e);
    private void OnGrantsMouseClick(object? sender, MouseEventArgs e) => _pathActionHelper.HandleGrantsMouseClick(e);
    private void OnTraverseMouseClick(object? sender, MouseEventArgs e) => _pathActionHelper.HandleTraverseMouseClick(e);
    private void OnAddFileClick(object? sender, EventArgs e) => _modificationHandler.AddFile();
    private void OnAddFolderClick(object? sender, EventArgs e) => _modificationHandler.AddFolder();
    private void OnAddTraverseFileClick(object? sender, EventArgs e) => _modificationHandler.AddTraverseFile();
    private void OnAddTraverseFolderClick(object? sender, EventArgs e) => _modificationHandler.AddTraverseFolder();
    private void OnRemoveClick(object? sender, EventArgs e) => _modificationHandler.RemoveSelected();
    private void OnUntrackGrantsClick(object? sender, EventArgs e) => _modificationHandler.UntrackGrants();
    private void OnUntrackTraverseClick(object? sender, EventArgs e) => _modificationHandler.UntrackTraverse();
    private void OnFixAclsClick(object? sender, EventArgs e) => _modificationHandler.FixAcls();

    private async void OnApplyClick(object? sender, EventArgs e) => await ExecuteApplyAsync();

    private async Task<bool> ExecuteApplyAsync()
    {
        try
        {
            _progressBar.Value = 0;
            _progressBar.Visible = true;
            var progress = new Progress<(int current, int total)>(p =>
            {
                if (!IsDisposed)
                {
                    _progressBar.Maximum = p.total;
                    _progressBar.Value = Math.Min(p.current, p.total);
                }
            });
            try
            {
                var outcome = await _applyHandler.ApplyAsync(progress,
                    enabled => _applyButton.Enabled = enabled,
                    enabled => Enabled = enabled,
                    RefreshGrids);
                var presentation = _applyPresenter.ShowResult(this, outcome);
                return !presentation.RetainPendingInput;
            }
            finally
            {
                _progressBar.Visible = false;
            }
        }
        catch (Exception ex)
        {
            _log.Error("Unexpected error during Apply", ex);
            MessageBox.Show($"An unexpected error occurred during Apply: {ex.Message}",
                "ACL Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private async void OnExportClick(object? sender, EventArgs e)
    {
        bool grantsTabActive = _tabControl.SelectedTab != _traverseTab;
        await _exportImport.Export(_grantsGrid, _traverseGrid, grantsTabActive);
    }

    private void OnImportClick(object? sender, EventArgs e) => _exportImport.Import(RefreshGrids);
    private void OnOpenFolderGrantsClick(object? sender, EventArgs e) => _pathActionHelper.OpenFolderGrants();
    private void OnCopyPathGrantsClick(object? sender, EventArgs e) => _pathActionHelper.CopyPathGrants();
    private void OnPropertiesGrantsClick(object? sender, EventArgs e) => _pathActionHelper.ShowPropertiesGrants();
    private void OnOpenFolderTraverseClick(object? sender, EventArgs e) => _pathActionHelper.OpenFolderTraverse();
    private void OnCopyPathTraverseClick(object? sender, EventArgs e) => _pathActionHelper.CopyPathTraverse();
    private void OnPropertiesTraverseClick(object? sender, EventArgs e) => _pathActionHelper.ShowPropertiesTraverse();
    private void OnGrantsGridMouseDown(object? sender, MouseEventArgs e)
    {
        _pathActionHelper.HandleGrantsRightClickDown(e);
        _mouseEventHandler.HandleGrantsMouseDown(e);
    }

    private void OnGrantsGridMouseMove(object? sender, MouseEventArgs e) => _mouseEventHandler.HandleGrantsMouseMove(e);

    private void OnTraverseGridMouseDown(object? sender, MouseEventArgs e)
    {
        _pathActionHelper.HandleTraverseRightClickDown(e);
        _mouseEventHandler.HandleTraverseMouseDown(e);
    }
    private void OnTraverseGridMouseMove(object? sender, MouseEventArgs e) => _mouseEventHandler.HandleTraverseMouseMove(e);
    private void OnGrantsGridMouseUp(object? sender, MouseEventArgs e) => _mouseEventHandler.HandleGrantsMouseUp(e);
    private void OnTraverseGridMouseUp(object? sender, MouseEventArgs e) => _mouseEventHandler.HandleTraverseMouseUp(e);
}
