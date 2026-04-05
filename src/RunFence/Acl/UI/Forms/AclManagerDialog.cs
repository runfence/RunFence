using System.ComponentModel;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.UI;

namespace RunFence.Acl.UI.Forms;

/// <summary>
/// ACL Manager dialog — manages per-path allow/deny grants and traverse paths for
/// a single account or AppContainer SID.
/// </summary>
public partial class AclManagerDialog : Form, IAclManagerGridRefresher, IAclManagerDialogHost
{
    private readonly ILoggingService _log;
    private readonly IAclPermissionService _aclPermission;
    private readonly IInteractiveUserResolver _interactiveUserResolver;
    private readonly TraverseAutoManager _traverseAutoManager;
    private readonly AclManagerGrantsHelper _grantsHelper;
    private readonly AclManagerTraverseHelper _traverseHelper;
    private readonly AclManagerDragDropHandler _dragDropHandler;
    private readonly AclManagerActionOrchestrator _actionHandler;
    private readonly GrantEntryNtfsOperations _ntfsOps;
    private readonly AclManagerApplyOrchestrator _applyHandler;
    private readonly AclManagerExportImport _exportImport;
    private readonly AclManagerSelectionHandler _selectionHandler;
    private readonly AclManagerModificationHandler _modificationHandler;
    private DropFilesInterceptor? _grantsDropInterceptor;
    private DropFilesInterceptor? _traverseDropInterceptor;
    private readonly AclManagerPendingChanges _pending = new();
    private readonly GridSortHelper _grantsSortHelper = new();
    private readonly GridSortHelper _traverseSortHelper = new();
    private bool _isContainer;

    public AclManagerDialog(
        IAclPermissionService aclPermission,
        ILoggingService log,
        IInteractiveUserResolver interactiveUserResolver,
        TraverseAutoManager traverseAutoManager,
        AclManagerGrantsHelper grantsHelper,
        AclManagerTraverseHelper traverseHelper,
        AclManagerDragDropHandler dragDropHandler,
        AclManagerActionOrchestrator actionHandler,
        GrantEntryNtfsOperations ntfsOps,
        AclManagerApplyOrchestrator applyHandler,
        AclManagerExportImport exportImport,
        AclManagerSelectionHandler selectionHandler,
        AclManagerModificationHandler modificationHandler)
    {
        _aclPermission = aclPermission;
        _log = log;
        _interactiveUserResolver = interactiveUserResolver;
        _traverseAutoManager = traverseAutoManager;
        _grantsHelper = grantsHelper;
        _traverseHelper = traverseHelper;
        _dragDropHandler = dragDropHandler;
        _actionHandler = actionHandler;
        _ntfsOps = ntfsOps;
        _applyHandler = applyHandler;
        _exportImport = exportImport;
        _selectionHandler = selectionHandler;
        _modificationHandler = modificationHandler;
    }

    public void Initialize(
        string sid,
        bool isContainer,
        string displayName)
    {
        _isContainer = isContainer;

        InitializeComponent();

        var groupSids = _aclPermission.ResolveAccountGroupSids(sid);

        _traverseAutoManager.Initialize(_pending, sid, groupSids);
        _ntfsOps.Initialize(sid, groupSids);

        _grantsHelper.Initialize(
            _grantsGrid, sid, isContainer, groupSids,
            _pending, _grantsSortHelper);

        _traverseHelper.Initialize(
            _traverseGrid, sid, _pending, isContainer, _traverseSortHelper);

        _dragDropHandler.Initialize(sid, _pending);

        _actionHandler.Initialize(sid, isContainer, this, _pending, this);

        string? interactiveUserSid = null;
        if (isContainer)
            interactiveUserSid = _interactiveUserResolver.GetInteractiveUserSid();

        _applyHandler.Initialize(
            _pending, sid, isContainer, this, interactiveUserSid);

        _exportImport.Initialize(_pending, sid, isContainer, this);

        var controls = BuildControls();

        _selectionHandler.Initialize(this, isContainer, _pending, controls, RefreshTraverseGrid);
        _modificationHandler.Initialize(this, sid, _pending, controls,
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
            ProgressBar = _progressBar,
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

    private void BuildDynamicContent(string displayName)
    {
        Text = $"ACL Manager \u2014 {displayName}";
        _formIcon = UiIconFactory.CreateDialogIcon("\U0001F4DC", Color.FromArgb(0x33, 0x66, 0x99));
        Icon = _formIcon;

        _ctxPropertiesGrants.Image = UiIconFactory.CreatePropertiesIcon();
        _ctxTraverseProperties.Image = UiIconFactory.CreatePropertiesIcon();

        // Position Close and Apply buttons
        _closeButton.Location = new Point(ClientSize.Width - _closeButton.Width - 8,
            ClientSize.Height - _closeButton.Height - 8);
        _applyButton.Location = new Point(_closeButton.Left - _applyButton.Width - 8,
            ClientSize.Height - _applyButton.Height - 8);

        AclManagerDialogGridSetup.BuildGrantsGrid(_grantsGrid, _isContainer);
        AclManagerDialogGridSetup.BuildTraverseGrid(_traverseGrid);

        // Suppress ComboBox cell formatting errors
        _grantsGrid.DataError += (_, e) => { e.ThrowException = false; };

        AclManagerDialogUiSetup.ConfigureToolbar(_toolStrip, _addFileButton, _addFolderButton, _scanButton, _removeButton, _fixAclsButton, _exportButton, _importButton);
        _toolStrip.PerformLayout();
        PositionTabControl();

        // Enable three-state column sorting with section-header awareness
        _grantsSortHelper.EnableThreeStateSorting(_grantsGrid, RefreshGrantsGrid, sectioned: true);
        _traverseSortHelper.EnableThreeStateSorting(_traverseGrid, RefreshTraverseGrid, sectioned: true);

        RefreshGrantsGrid();
        RefreshTraverseGrid();

        _grantsGrid.HandleCreated += (_, _) =>
            _grantsDropInterceptor = new DropFilesInterceptor(_grantsGrid.Handle, _modificationHandler.HandleGrantsFileDrop);
        _traverseGrid.HandleCreated += (_, _) =>
            _traverseDropInterceptor = new DropFilesInterceptor(_traverseGrid.Handle, _modificationHandler.HandleTraverseFileDrop);

        Resize += OnResize;
        Shown += OnShown;
    }

    private void OnShown(object? sender, EventArgs e)
    {
        _grantsGrid.ClearSelection();
        _traverseGrid.ClearSelection();
        _selectionHandler.UpdateActionButtons();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        bool cancel = _selectionHandler.HandleFormClosing();
        if (!cancel)
            _modificationHandler.CancelScanCts?.Cancel();
        if (cancel)
        {
            e.Cancel = true;
            return;
        }

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
        int toolbarHeight = _toolStrip.PreferredSize.Height;
        _tabControl.Location = new Point(0, toolbarHeight);
        _tabControl.Size = new Size(ClientSize.Width, ClientSize.Height - toolbarHeight - _closeButton.Height - 16);
    }

    private void OnResize(object? sender, EventArgs e)
    {
        _closeButton.Location = new Point(ClientSize.Width - _closeButton.Width - 8,
            ClientSize.Height - _closeButton.Height - 8);
        _applyButton.Location = new Point(_closeButton.Left - _applyButton.Width - 8,
            ClientSize.Height - _applyButton.Height - 8);
        PositionTabControl();
    }

    // --- Named handlers for Designer.cs event wiring ---
    // These thin delegates are required because Designer.cs wires events by method name.

    private void OnTabChanged(object? sender, EventArgs e) => _selectionHandler.HandleTabChanged();
    private void OnGrantsSelectionChanged(object? sender, EventArgs e) => _selectionHandler.HandleSelectionChanged();
    private void OnTraverseSelectionChanged(object? sender, EventArgs e) => _selectionHandler.HandleSelectionChanged();
    private void OnGrantsCellValueChanged(object? sender, DataGridViewCellEventArgs e) => _selectionHandler.HandleGrantsCellValueChanged(e);
    private void OnGrantsCurrentCellDirtyStateChanged(object? sender, EventArgs e) => _selectionHandler.HandleGrantsDirtyStateChanged();
    private void OnScanFolderClick(object? sender, EventArgs e) => _modificationHandler.ScanFolder();
    private void OnGrantsContextMenuOpening(object? sender, CancelEventArgs e) => _selectionHandler.HandleGrantsContextMenuOpening();
    private void OnTraverseContextMenuOpening(object? sender, CancelEventArgs e) => _selectionHandler.HandleTraverseContextMenuOpening();
    private void OnGridKeyDown(object? sender, KeyEventArgs e) => _selectionHandler.HandleGridKeyDown(e);
    private void OnGrantsMouseClick(object? sender, MouseEventArgs e) => _selectionHandler.HandleGrantsMouseClick(e);
    private void OnTraverseMouseClick(object? sender, MouseEventArgs e) => _selectionHandler.HandleTraverseMouseClick(e);
    private void OnAddFileClick(object? sender, EventArgs e) => _modificationHandler.AddFile();
    private void OnAddFolderClick(object? sender, EventArgs e) => _modificationHandler.AddFolder();
    private void OnAddTraverseFileClick(object? sender, EventArgs e) => _modificationHandler.AddTraverseFile();
    private void OnAddTraverseFolderClick(object? sender, EventArgs e) => _modificationHandler.AddTraverseFolder();
    private void OnRemoveClick(object? sender, EventArgs e) => _modificationHandler.RemoveSelected();
    private void OnUntrackGrantsClick(object? sender, EventArgs e) => _modificationHandler.UntrackGrants();
    private void OnUntrackTraverseClick(object? sender, EventArgs e) => _modificationHandler.UntrackTraverse();
    private void OnFixAclsClick(object? sender, EventArgs e) => _modificationHandler.FixAcls();

    private async void OnApplyClick(object? sender, EventArgs e)
    {
        try
        {
            await _applyHandler.ApplyAsync(_progressBar,
                enabled => _applyButton.Enabled = enabled,
                enabled => Enabled = enabled,
                RefreshGrids);
        }
        catch (Exception ex)
        {
            _log.Error("Unexpected error during Apply", ex);
            MessageBox.Show($"An unexpected error occurred during Apply: {ex.Message}",
                "ACL Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnExportClick(object? sender, EventArgs e)
    {
        bool grantsTabActive = _tabControl.SelectedTab != _traverseTab;
        _exportImport.Export(_grantsGrid, _traverseGrid, grantsTabActive);
    }

    private void OnImportClick(object? sender, EventArgs e) => _exportImport.Import(RefreshGrids);
    private void OnOpenFolderGrantsClick(object? sender, EventArgs e) => _selectionHandler.OpenFolderGrants();
    private void OnCopyPathGrantsClick(object? sender, EventArgs e) => _selectionHandler.CopyPathGrants();
    private void OnPropertiesGrantsClick(object? sender, EventArgs e) => _selectionHandler.ShowPropertiesGrants();
    private void OnOpenFolderTraverseClick(object? sender, EventArgs e) => _selectionHandler.OpenFolderTraverse();
    private void OnCopyPathTraverseClick(object? sender, EventArgs e) => _selectionHandler.CopyPathTraverse();
    private void OnPropertiesTraverseClick(object? sender, EventArgs e) => _selectionHandler.ShowPropertiesTraverse();
    private void OnGrantsGridMouseDown(object? sender, MouseEventArgs e) => _modificationHandler.HandleGrantsMouseDown(e);
    private void OnGrantsGridMouseMove(object? sender, MouseEventArgs e) => _modificationHandler.HandleGrantsMouseMove(e);
    private void OnTraverseGridMouseDown(object? sender, MouseEventArgs e) => _modificationHandler.HandleTraverseMouseDown(e);
    private void OnTraverseGridMouseMove(object? sender, MouseEventArgs e) => _modificationHandler.HandleTraverseMouseMove(e);
    private void OnGrantsGridMouseUp(object? sender, MouseEventArgs e) => _modificationHandler.HandleGrantsMouseUp(e);
    private void OnTraverseGridMouseUp(object? sender, MouseEventArgs e) => _modificationHandler.HandleTraverseMouseUp(e);
}