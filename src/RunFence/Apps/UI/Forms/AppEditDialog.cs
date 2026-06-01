using RunFence.Account.UI;
using RunFence.Account.UI.AppContainer;
using RunFence.Acl.UI;
using RunFence.Acl.UI.Forms;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launching.Resolution;
using RunFence.Persistence;
using RunFence.UI;
using RunFence.UI.Forms;

namespace RunFence.Apps.UI.Forms;

public partial class AppEditDialog : RunFence.UI.Forms.ContextHelpForm, IAclConfigContextProvider, IAppEditBrowseResultReceiver, IHandlerAssociationsHost, IAppEditDialogSnapshotView, IAppEditDialogSectionsView
{
    private ToolTip? _configToolTip;
    private bool _hasLoadedConfigs;
    private bool _isFolder;
    private string? _lastBasicPromptedPath;

    private AppEntry? _existing;
    private List<CredentialEntry> _credentials = null!;
    private List<AppEntry> _existingApps = null!;
    private IReadOnlyDictionary<string, string>? _sidNames;
    private AppDatabase? _database;
    private AppEditDialogCommandContext _commandContext = null!;

    private readonly IAppConfigService _appConfigService;

    private readonly AclConfigSection _aclSection;
    private IpcCallerSection _ipcSection = null!;
    private EnvVarsSection _envVarsSection = null!;
    private readonly HandlerAssociationsSection _associationsSection;
    private PathPrefixesSection _appPrefixesSection = null!;
    private readonly AppEditBrowseHelper _browseHelper;
    private readonly AppEditAccountSwitchHandler _switchHandler;
    private readonly IExecutablePathResolver _executablePathResolver;
    private readonly AppEditDialogInitializationBinder _initializationBinder;
    private readonly AppEditDialogSubmitController _submitController;
    private readonly ILoggingService _log;
    private readonly IUserConfirmationService _userConfirmationService;
    private readonly IHandlerAssociationMutationService _handlerAssociationMutationService;
    private readonly HandlerAssociationsChildDialogCoordinator _handlerAssociationsChildDialogCoordinator;
    private readonly IUiIconService _uiIconService;
    private readonly AppEditDialogSnapshotProvider _snapshotProvider;
    private readonly AppEntryEditPathRepairSuggester _pathRepairSuggester;
    private AppEditDialogSectionPresenter? _sectionPresenter;
    private AppEditDialogMode _mode;

    public AppEntry Result { get; private set; } = null!;
    public bool LaunchNow => _launchNowCheckBox.Checked;
    public bool HasUnsavedInMemoryMutations { get; private set; }
    public AppEntry? ExistingApp => _existing;
    public IReadOnlyList<CredentialEntry> Credentials => _credentials;
    public IReadOnlyList<AppEntry> ExistingApps => _existingApps;
    public IReadOnlyDictionary<string, string>? SidNames => _sidNames;
    public AppDatabase? DatabaseOrNull => _database;

    /// <summary>Returns null for main config, or the full path for an additional config.</summary>
    public string? SelectedConfigPath
    {
        get
        {
            return _configComboBox.SelectedItem is ConfigComboItem item ? item.Path : null;
        }
    }

    // IAclConfigContextProvider implementation
    string IAclConfigContextProvider.GetExePath() => _filePathTextBox.Text.Trim();

    string? IAclConfigContextProvider.GetSelectedAccountSid() => _accountComboBox.SelectedItem switch
    {
        CredentialDisplayItem cdi => cdi.Credential.Sid,
        AppContainerDisplayItem acdi => acdi.ContainerSid,
        _ => null
    };

    bool IAclConfigContextProvider.IsContainerSelected() => _accountComboBox.SelectedItem is AppContainerDisplayItem;

    void IAclConfigContextProvider.OnSidNameLearned(string sid, string name)
    {
        _database?.UpdateSidName(sid, name);
    }

    public void SetAssociations(IReadOnlyList<HandlerAssociationItem>? associations)
    {
        if (_sectionPresenter == null)
            return;

        var snapshot = _snapshotProvider.CaptureInputSnapshot(this, this) with
        {
            HandlerMappings = associations?.ToList()
        };
        _sectionPresenter.InitializeSections(snapshot);
        _sectionPresenter.ApplySectionVisibility(_mode, _isFolder);
    }

    public AppEditDialog(
        IAppConfigService appConfigService,
        AclConfigSection aclSection,
        AppEditBrowseHelper browseHelper,
        AppEditAccountSwitchHandler switchHandler,
        AppEditDialogSubmitController submitController,
        ILoggingService log,
        IExecutablePathResolver executablePathResolver,
        HandlerAssociationsSection associationsSection,
        AppEditDialogInitializationBinder initializationBinder,
        IUserConfirmationService userConfirmationService,
        IHandlerAssociationMutationService handlerAssociationMutationService,
        HandlerAssociationsChildDialogCoordinator handlerAssociationsChildDialogCoordinator,
        IUiIconService uiIconService,
        AppEditDialogSnapshotProvider snapshotProvider,
        AppEntryEditPathRepairSuggester pathRepairSuggester)
    {
        _appConfigService = appConfigService;
        _aclSection = aclSection;
        _browseHelper = browseHelper;
        _switchHandler = switchHandler;
        _submitController = submitController;
        _log = log;
        _executablePathResolver = executablePathResolver;
        _associationsSection = associationsSection;
        _initializationBinder = initializationBinder;
        _userConfirmationService = userConfirmationService;
        _handlerAssociationMutationService = handlerAssociationMutationService;
        _handlerAssociationsChildDialogCoordinator = handlerAssociationsChildDialogCoordinator;
        _uiIconService = uiIconService;
        _snapshotProvider = snapshotProvider;
        _pathRepairSuggester = pathRepairSuggester;
        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
    }

    /// <summary>
    /// Initializes per-use dialog data and configures UI. Must be called before <see cref="Form.ShowDialog()"/>.
    /// </summary>
    public void Initialize(
        AppEntry? existing,
        List<CredentialEntry> credentials,
        List<AppEntry> existingApps,
        AppEditDialogCommandContext commandContext,
        AppEditDialogOptions? options = null,
        IReadOnlyDictionary<string, string>? sidNames = null,
        AppDatabase? database = null)
    {
        options ??= new AppEditDialogOptions();

        _existing = existing;
        _credentials = credentials;
        _existingApps = existingApps;
        _commandContext = commandContext;
        _sidNames = sidNames;
        _database = database;
        _mode = existing != null ? AppEditDialogMode.Edit : AppEditDialogMode.New;

        _initializationBinder.BuildDynamicContent(this, options);
        if (_existing != null)
        {
            _initializationBinder.BindExistingApp(this);
            _pathRepairSuggester.SuggestIfNeeded(_existing, this);
        }
        else
            _initializationBinder.BindNewApp(this, options);
    }

    public void BuildDynamicContentCore(
        AppEditDialogOptions options,
        IReadOnlyList<object> accountItems,
        IReadOnlyList<ConfigComboItem> configItems,
        IpcCallerSection ipcSection)
    {
        _ipcSection = ipcSection;
        _ipcSection.SetSidNames(_sidNames, _database != null ? (sid, name) => _database.UpdateSidName(sid, name) : null);
        _switchHandler.Initialize(_privilegeLevelComboBox, _aclSection);

        // Add IpcCallerSection to its container (in Access tab — fixed position above AclSection)
        _ipcSection.Dock = DockStyle.Fill;
        _ipcContainer.Controls.Add(_ipcSection);
        _ipcSection.SetEnabled(false);

        // Add AclConfigSection below the IPC section (fixed y; grows downward into AutoScroll area)
        _aclSection.Location = new Point(0, 160);
        _aclSection.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _tabAccess.Controls.Add(_aclSection);
        _aclSection.LayoutChanged += () => _tabAccess.PerformLayout();

        // Add EnvVarsSection to Parameters tab
        _envVarsSection = new EnvVarsSection();
        _envVarsSection.Dock = DockStyle.Fill;
        _envVarsContainer.Controls.Add(_envVarsSection);

        // Add HandlerAssociationsSection and PathPrefixesSection to Associations tab as equal 50/50 split.
        _appPrefixesSection = new PathPrefixesSection { GroupBoxTitle = "Path Prefixes" };
        _associationsSection.Initialize(
            _handlerAssociationMutationService,
            _handlerAssociationsChildDialogCoordinator,
            _uiIconService,
            this);
        _associationsSection.Dock = DockStyle.Fill;
        _appPrefixesSection.Dock = DockStyle.Fill;
        var assocLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
        assocLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        assocLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        assocLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        assocLayout.Controls.Add(_associationsSection, 0, 0);
        assocLayout.Controls.Add(_appPrefixesSection, 0, 1);
        _tabAssociations.Controls.Add(assocLayout);
        _configToolTip ??= new ToolTip();
        _sectionPresenter = new AppEditDialogSectionPresenter(this, this, _snapshotProvider);
        // Populate privilege level combobox before account combo (switch handler reads it on selection change)
        _privilegeLevelComboBox.Items.Clear();
        _privilegeLevelComboBox.Items.Add("(Account default)");
        _privilegeLevelComboBox.Items.Add("Highest Allowed");
        _privilegeLevelComboBox.Items.Add("High Integrity");
        _privilegeLevelComboBox.Items.Add("Basic");
        _privilegeLevelComboBox.Items.Add("Isolated");
        _privilegeLevelComboBox.Items.Add("Low Integrity");
        _privilegeLevelComboBox.SelectedIndex = 0;

        // Populate account and config combo boxes
        PopulateAccountCombo(accountItems);
        PopulateConfigCombo(configItems);

        // Skip the divider if selected
        _accountComboBox.SelectedIndexChanged += OnAccountComboSelectedIndexChanged;

        if (_accountComboBox.Items.Count > 0 &&
            _existing == null &&
            options.AccountSid == null &&
            options.ContainerName == null)
            _accountComboBox.SelectedIndex = 0;

        _hasLoadedConfigs = _appConfigService.HasLoadedConfigs;
        _configComboBox.Enabled = _hasLoadedConfigs;
        if (!_hasLoadedConfigs)
            _configToolTip.SetToolTip(_configComboBox, "Load additional configs in Options to save to a different file");

        // Runtime-dependent values
        Text = _existing != null ? "Edit Application" : "Add Application";
        _launcherPathTextBox.Text = Path.Combine(AppContext.BaseDirectory, PathConstants.LauncherExeName);
        _launcherArgsTextBox.Text = _existing != null ? _existing.Id : "(assigned on save)";
        _launcherArgsTextBox.ForeColor = _existing != null ? SystemColors.ControlText : SystemColors.GrayText;
        _removeButton.Visible = _existing != null;

        // Position launch now checkbox relative to OK button
        _launchNowCheckBox.Location = new Point(_okButton.Left - _launchNowCheckBox.PreferredSize.Width - 8, _okButton.Top + 5);

        AcceptButton = _okButton;
        CancelButton = _cancelButton;
        _launchNowCheckBox.Checked = options.LaunchNow;

        // Set inline button heights and tops to match adjacent text boxes for DPI correctness
        _browseButton.Height = _filePathTextBox.PreferredHeight;
        _browseButton.Top = _filePathTextBox.Top;
        _browseFolderButton.Height = _filePathTextBox.PreferredHeight;
        _browseFolderButton.Top = _filePathTextBox.Top;
        _discoverButton.Height = _filePathTextBox.PreferredHeight;
        _discoverButton.Top = _filePathTextBox.Top;
        _workingDirBrowseButton.Height = _workingDirTextBox.PreferredHeight;
        _workingDirBrowseButton.Top = _workingDirTextBox.Top;

        // Set up AclConfigSection context after controls are created
        _aclSection.SetContext(new AclConfigContext(
            Provider: this,
            ExistingApps: _existingApps,
            CurrentAppId: _existing?.Id,
            SidNames: _sidNames));

        RegisterContextHelp();
        RefreshSectionState();
    }

    private void RegisterContextHelp()
    {
        SetContextHelp(_configComboBox, ContextHelpTextCatalog.AppEdit_ExtraConfigStore);
        SetContextHelp(_privilegeLevelComboBox, ContextHelpTextCatalog.Launch_PrivilegeLevel);
        SetContextHelp(_manageShortcutsCheckBox, ContextHelpTextCatalog.RunAs_Shortcut);

        SetContextHelp(_defaultArgsTextBox, ContextHelpTextCatalog.Launch_Arguments);
        SetContextHelp(_allowPassArgsCheckBox, ContextHelpTextCatalog.Launch_Arguments);
        SetContextHelp(_argsTemplateTextBox, ContextHelpTextCatalog.Launch_Arguments);
        // Launcher access override details are provided by the nested caller section as a whole.
        SetContextHelp(_associationsSection, ContextHelpTextCatalog.App_HandlerMappings);
        SetContextHelp(_appPrefixesSection, ContextHelpTextCatalog.App_PathPrefixes);
        SetContextHelp(_envVarsContainer, ContextHelpTextCatalog.App_EnvironmentVariables);

        _aclSection.RegisterContextHelp(this);
        _ipcSection.RegisterContextHelp(this, ContextHelpTextCatalog.AppEdit_LauncherAccessOverride);
    }

    // TabControl does not size its TabPages until the handle is created, so Anchor=Right on controls
    // added directly to a TabPage computes a negative right-delta (parent width = 0 at init time),
    // making them ~2x the form width at runtime. Fix: Anchor=Left only + set widths once in OnLoad.
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var tw = _tabAccess.ClientSize.Width; // all tab pages share the same tab control display rect
        _ipcContainer.Width = tw - _ipcContainer.Left * 2;
        _aclSection.Width = tw;
        _envVarsContainer.Width = tw - _envVarsContainer.Left * 2;
    }

    private int _lastAccountComboIndex = -1;

    private void OnAccountComboSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (SeparatorSkipHelper.HandleSeparatorSkip(
                _accountComboBox.SelectedItem,
                _accountComboBox.SelectedIndex,
                _accountComboBox.Items.Count,
                i => _accountComboBox.SelectedIndex = i,
                ref _lastAccountComboIndex))
            return;

        _sectionPresenter?.RefreshForSelectedAccount(((IAppEditDialogSnapshotView)this).SelectedAccountSid);
        _switchHandler.HandleSelectionChanged(_accountComboBox.SelectedItem);
    }

    private void OnAssociationsSectionChanged()
    {
        // Auto-enable AllowPassingArguments when associations are added
        if (_associationsSection.GetAssociations()?.Count > 0)
            _allowPassArgsCheckBox.Checked = true;
    }

    void IHandlerAssociationsHost.RefreshMappings() => OnAssociationsSectionChanged();

    AppEntry? IHandlerAssociationsHost.GetSelectedApp() => _existing;

    HandlerAssociationMode IHandlerAssociationsHost.GetCurrentAssociationMode() => HandlerAssociationMode.App;

    private void PopulateAccountCombo(IReadOnlyList<object> accountItems)
    {
        _accountComboBox.Items.Clear();
        foreach (var item in accountItems)
            _accountComboBox.Items.Add(item);
    }

    private void PopulateConfigCombo(IReadOnlyList<ConfigComboItem> configItems)
    {
        _configComboBox.Items.Clear();
        foreach (var item in configItems)
            _configComboBox.Items.Add(item);
        _configComboBox.SelectedIndex = 0;
    }

    private static bool SelectComboItem<T>(ComboBox combo, Func<T, bool> match, int startIndex = 0)
        where T : class
    {
        for (int i = startIndex; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is T item && match(item))
            {
                combo.SelectedIndex = i;
                return true;
            }
        }

        return false;
    }

    private string? _preGeneratedId;

    public void SetPreGeneratedId(string appId)
    {
        _preGeneratedId = appId;
        _launcherArgsTextBox.Text = _preGeneratedId;
        _launcherArgsTextBox.ForeColor = SystemColors.ControlText;
    }

    private void OnFilePathChanged(object? sender, EventArgs e)
    {
        var text = _filePathTextBox.Text;
        var isUrl = PathHelper.IsUrlScheme(text);
        if (isUrl)
        {
            _isFolder = false;
            _filePathLabel.Text = "File Path or URL:";
        }

        _aclSection.SetExePath(text, _isFolder);
        UpdateEditorState();
    }

    private void SetFolderMode(bool isFolder)
    {
        _isFolder = isFolder;
        _filePathLabel.Text = isFolder ? "Folder Path:" : "File Path or URL:";
        _aclSection.SetExePath(_filePathTextBox.Text, _isFolder);
        UpdateEditorState();
    }

    private void UpdateEditorState()
    {
        var isUrl = PathHelper.IsUrlScheme(_filePathTextBox.Text);
        _defaultArgsTextBox.Enabled = !isUrl && !_isFolder;
        _allowPassArgsCheckBox.Enabled = !isUrl && !_isFolder;
        _argsTemplateTextBox.Enabled = !isUrl && !_isFolder;
        _workingDirTextBox.Enabled = !isUrl && !_isFolder;
        _workingDirBrowseButton.Enabled = !isUrl && !_isFolder;
        _allowPassWorkDirCheckBox.Enabled = !isUrl && !_isFolder;
        RefreshSectionState();
    }

    // IAppEditBrowseResultReceiver implementation
    string IAppEditBrowseResultReceiver.GetAppName() => _nameTextBox.Text;
    string? IAppEditBrowseResultReceiver.GetSelectedAccountSid()
        => _accountComboBox.SelectedItem is CredentialDisplayItem credentialDisplay
            ? credentialDisplay.Credential.Sid
            : null;

    void IAppEditBrowseResultReceiver.SetFilePath(string path) => _filePathTextBox.Text = path;
    void IAppEditBrowseResultReceiver.SetAppName(string name) => _nameTextBox.Text = name;
    void IAppEditBrowseResultReceiver.SetFolderMode(bool isFolder) => SetFolderMode(isFolder);

    void IAppEditBrowseResultReceiver.SetWorkingDir(string path)
    {
        if (string.IsNullOrWhiteSpace(_workingDirTextBox.Text))
            _workingDirTextBox.Text = path;
    }

    void IAppEditBrowseResultReceiver.SetDefaultArgs(string args)
    {
        if (string.IsNullOrWhiteSpace(_defaultArgsTextBox.Text))
            _defaultArgsTextBox.Text = args;
    }

    bool IAppEditBrowseResultReceiver.CanSuggestBasicPrivilegeLevel()
    {
        if (!_privilegeLevelComboBox.Enabled)
            return false;
        var selected = PrivilegeLevelComboHelper.IndexToMode(_privilegeLevelComboBox.SelectedIndex);
        if (selected is PrivilegeLevel.HighestAllowed or PrivilegeLevel.HighIntegrity or PrivilegeLevel.Basic)
            return false;
        // "(Account default)" — also check what the account default resolves to
        if (selected == null)
        {
            var sid = (_accountComboBox.SelectedItem as CredentialDisplayItem)?.Credential.Sid;
            var accountDefault = sid != null ? _database?.GetAccount(sid)?.PrivilegeLevel : null;
            if (accountDefault is PrivilegeLevel.HighestAllowed or PrivilegeLevel.HighIntegrity or PrivilegeLevel.Basic)
                return false;
        }
        return true;
    }

    void IAppEditBrowseResultReceiver.SetPrivilegeLevel(PrivilegeLevel? level)
        => _privilegeLevelComboBox.SelectedIndex = PrivilegeLevelComboHelper.ModeToIndex(level);

    private void OnBrowseClick(object? sender, EventArgs e)
        => _browseHelper.BrowseAndApplyFile(this);

    private void OnBrowseFolderClick(object? sender, EventArgs e)
        => _browseHelper.BrowseAndApplyFolder(this);

    private void OnWorkingDirBrowseClick(object? sender, EventArgs e)
    {
        var path = _browseHelper.BrowseWorkingDir(_workingDirTextBox.Text);
        if (path != null)
            _workingDirTextBox.Text = path;
    }

    private async void OnDiscoverClick(object? sender, EventArgs e)
    {
        await HandleDiscoverAsync();
    }

    private async void OnRemoveClick(object? sender, EventArgs e)
    {
        await HandleRemoveAsync();
    }

    private void OnIpcOverrideChanged(object? sender, EventArgs e)
    {
        _ipcSection.SetEnabled(_overrideIpcCallersCheckBox.Checked);
    }

    public void ApplyExistingInitializationCore(AppEditInitializationModel model)
    {
        ApplyExistingState(model.State);
        LockFilePathControls();

        _aclSection.PopulateFromExisting(model.AclState);
        if (model.EnvironmentVariables?.Count > 0)
            _envVarsSection.SetItems(model.EnvironmentVariables.ToDictionary(
                kv => kv.Key,
                kv => kv.Value,
                StringComparer.OrdinalIgnoreCase));

        if (model.IpcCallers != null)
        {
            _ipcSection.SetCallers(model.IpcCallers.ToList());
            _ipcSection.SetEnabled(true);
        }

        if (_sectionPresenter != null)
        {
            var snapshot = _snapshotProvider.CaptureInputSnapshot(this, this) with
            {
                HandlerMappings = model.Associations?.ToList(),
                AppPathPrefixes = model.PathPrefixes?.ToList()
            };
            _sectionPresenter.InitializeSections(snapshot);
            _sectionPresenter.ApplySectionVisibility(_mode, _isFolder);
        }
    }

    private void ApplyExistingState(AppEditState state)
    {
        _nameTextBox.Text = state.Name;
        _isFolder = state.IsFolder;
        if (_isFolder)
            _filePathLabel.Text = "Folder Path:";
        _filePathTextBox.Text = state.ExePath;
        _defaultArgsTextBox.Text = state.DefaultArguments;
        _allowPassArgsCheckBox.Checked = state.AllowPassingArguments;
        _argsTemplateTextBox.Text = state.ArgumentsTemplate;
        _workingDirTextBox.Text = state.WorkingDirectory;
        _allowPassWorkDirCheckBox.Checked = state.AllowPassingWorkingDirectory;
        _manageShortcutsCheckBox.Checked = state.ManageShortcuts;
        _privilegeLevelComboBox.SelectedIndex = PrivilegeLevelComboHelper.ModeToIndex(state.SelectedPrivilegeLevel);
        _overrideIpcCallersCheckBox.Checked = state.OverrideIpcCallers;
    }

    private void LockFilePathControls()
    {
        _filePathTextBox.ReadOnly = false;
        _filePathTextBox.BackColor = SystemColors.Window;
        _browseButton.Visible = true;
        _browseFolderButton.Visible = true;
        _discoverButton.Visible = true;
    }

    public bool SelectAccountComboForExisting(AppEditExistingAccountSelection selection)
    {
        _accountComboBox.SelectedIndex = -1;

        if (selection.AppContainerName != null)
        {
            return SelectComboItem<AppContainerDisplayItem>(_accountComboBox,
                ci => string.Equals(ci.Container.Name, selection.AppContainerName, StringComparison.OrdinalIgnoreCase));
        }

        return SelectComboItem<CredentialDisplayItem>(_accountComboBox,
            item => SidComparer.SidEquals(item.Credential.Sid, selection.AccountSid));
    }

    public void SelectConfigAndAclPath(AppEditInitializationModel model)
    {
        // SetExePath rebuilds the folder depth combo and updates ACL state.
        // SelectFolderDepth must come after to override the default depth.
        _aclSection.SetExePath(model.State.ExePath, model.State.IsFolder);
        _aclSection.SelectFolderDepth(model.AclState.FolderAclDepth);

        _configComboBox.SelectedIndex = 0;
        if (model.SelectedConfigPath == null)
            return;

        var selected = SelectComboItem<ConfigComboItem>(_configComboBox,
            item => string.Equals(item.Path, model.SelectedConfigPath, StringComparison.OrdinalIgnoreCase),
            startIndex: 1);
        if (!selected)
        {
            var currentConfigItem = new ConfigComboItem(model.SelectedConfigPath);
            _configComboBox.Items.Add(currentConfigItem);
            _configComboBox.SelectedItem = currentConfigItem;
        }
    }

    public void SelectConfigPath(string? configPath)
    {
        if (!_hasLoadedConfigs)
            return;

        _configComboBox.SelectedIndex = 0;
        if (configPath != null)
        {
            SelectComboItem<ConfigComboItem>(_configComboBox,
                item => string.Equals(item.Path, configPath, StringComparison.OrdinalIgnoreCase),
                startIndex: 1);
        }
    }

    public void SelectAccountBySid(string sid)
    {
        SelectComboItem<CredentialDisplayItem>(_accountComboBox,
            ci => SidComparer.SidEquals(ci.Credential.Sid, sid));
    }

    public void SelectAccountByContainerName(string containerName)
    {
        SelectComboItem<AppContainerDisplayItem>(_accountComboBox,
            acdi => string.Equals(acdi.Container.Name, containerName, StringComparison.OrdinalIgnoreCase));
    }

    public void SetExePathAndDefaultName(string exePath)
    {
        _filePathTextBox.Text = exePath;
        _nameTextBox.Text = Path.GetFileNameWithoutExtension(exePath);
    }

    public void ApplyNewOptions(AppEditDialogOptions options)
    {
        _aclSection.RestrictAcl = options.RestrictAcl;
        _manageShortcutsCheckBox.Checked = options.ManageShortcuts;
        _privilegeLevelComboBox.SelectedIndex = PrivilegeLevelComboHelper.ModeToIndex(options.PrivilegeLevel);
    }

    public void AddAccountItemAndSelect(CredentialDisplayItem item)
    {
        _accountComboBox.Items.Add(item);
        _accountComboBox.SelectedItem = item;
    }

    private void TryPromptBasicForCurrentPath()
    {
        if (_existing != null || _isFolder)
            return;
        var path = _filePathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path) ||
            string.Equals(path, _lastBasicPromptedPath, StringComparison.OrdinalIgnoreCase))
            return;
        _lastBasicPromptedPath = path;
        var sid = (_accountComboBox.SelectedItem as CredentialDisplayItem)?.Credential.Sid;
        var resolvedPath = _executablePathResolver.TryResolvePath(
            path,
            ExecutablePathResolutionContext.CurrentProcess(sid));
        _browseHelper.PromptBasicIfNeeded(resolvedPath ?? path, this);
    }

    private async void OnOkClick(object? sender, EventArgs e)
    {
        await HandleOkAsync();
    }

    private async Task HandleDiscoverAsync()
    {
        try
        {
            if (IsDisposed || Disposing)
                return;

            _tabControl.Enabled = false;
            _buttonPanel.Enabled = false;
            Cursor = Cursors.WaitCursor;
            await _browseHelper.DiscoverAndApplyAsync(this, () => !IsDisposed && !Disposing);
        }
        catch (Exception ex)
        {
            _log.Error("App edit discovery failed", ex);
            if (!IsDisposed && !Disposing)
            {
                ShowStatusError(ex.Message);
            }
        }
        finally
        {
            if (!IsDisposed && !Disposing)
            {
                Cursor = Cursors.Default;
                _buttonPanel.Enabled = true;
                _tabControl.Enabled = true;
            }
        }
    }

    private async Task HandleRemoveAsync()
    {
        if (_existing == null || IsDisposed || Disposing)
            return;

        var removeAsync = _commandContext.RemoveAsync;
        if (removeAsync == null)
            return;

        var confirmed = _userConfirmationService.Confirm(
            AppEntryHelper.GetRemoveConfirmationMessage(_existing),
            "Confirm");
        if (!confirmed)
            return;

        try
        {
            Enabled = false;
            await removeAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error("App edit remove failed", ex);
            if (!IsDisposed && !Disposing)
            {
                ShowStatusError(ex.Message);
            }
        }
        finally
        {
            if (!IsDisposed && !Disposing)
                Enabled = true;
        }
    }

    private async Task HandleOkAsync()
    {
        try
        {
            if (IsDisposed || Disposing)
                return;

            TryPromptBasicForCurrentPath();
            if (IsDisposed || Disposing)
                return;

            ClearStatus();
            var submitResult = _submitController.Submit(new AppEditDialogSubmitRequest(
                Input: _snapshotProvider.CaptureInputSnapshot(this, this)));
            ApplySubmitResult(submitResult);

            if (submitResult.Result == null || IsDisposed || Disposing)
                return;

            var currentAssociations = _snapshotProvider.CaptureInputSnapshot(this, this).HandlerMappings ?? [];
            Enabled = false;
            await ApplyCurrentResultAsync(submitResult.Result, currentAssociations);
        }
        catch (Exception ex)
        {
            _log.Error("App edit OK failed", ex);
            if (!IsDisposed && !Disposing)
            {
                ShowStatusError(ex.Message);
            }
        }
        finally
        {
            if (!IsDisposed && !Disposing && Visible)
                Enabled = true;
        }
    }

    private async Task ApplyCurrentResultAsync(AppEntry result, IReadOnlyList<HandlerAssociationItem> currentAssociations)
    {
        _statusLabel.ForeColor = SystemColors.ControlText;
        _statusLabel.Text = "Saving...";
        var submitResult = await _submitController.ApplyExistingResultAsync(new AppEditDialogApplyRequest(
            Result: result,
            Database: _database,
            SelectedConfigPath: SelectedConfigPath,
            CurrentAssociations: currentAssociations,
            ApplyAsync: _commandContext.ApplyAsync));

        if (IsDisposed)
            return;

        ApplySubmitResult(submitResult);
    }

    private void ApplySubmitResult(AppEditDialogSubmitResult submitResult)
    {
        if (submitResult.Result != null)
            Result = submitResult.Result;

        HasUnsavedInMemoryMutations = submitResult.HasUnsavedMutations;

        if (submitResult.StatusText != null)
        {
            if (submitResult.StatusText.Length == 0)
                ClearStatus();
            else
            {
                if (submitResult.StatusIsError)
                    _statusLabel.ForeColor = Color.Red;
                else if (_statusLabel.ForeColor.ToArgb() == Color.Red.ToArgb())
                    _statusLabel.ForeColor = SystemColors.ControlText;
                _statusLabel.Text = submitResult.StatusText;
            }
        }

        if (!string.IsNullOrWhiteSpace(submitResult.NotificationMessage))
        {
            MessageBox.Show(
                this,
                submitResult.NotificationMessage,
                submitResult.NotificationIsWarning ? "Saved With Warning" : "Notification",
                MessageBoxButtons.OK,
                submitResult.NotificationIsWarning ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        if (submitResult.DialogResult == null)
            return;

        DialogResult = submitResult.DialogResult.Value;
        Close();
    }

    private void ClearStatus()
    {
        _statusLabel.ForeColor = SystemColors.ControlText;
        _statusLabel.Text = string.Empty;
    }

    private void ShowStatusError(string? message)
    {
        Enabled = true;
        _statusLabel.ForeColor = Color.Red;
        _statusLabel.Text = $"Failed: {message}";
    }

    private void RefreshSectionState()
    {
        if (_sectionPresenter == null)
            return;

        _sectionPresenter.InitializeSections(_snapshotProvider.CaptureInputSnapshot(this, this));
        _sectionPresenter.ApplySectionVisibility(_mode, _isFolder);
    }

    string? IAppEditDialogSnapshotView.SelectedAccountSid => _accountComboBox.SelectedItem switch
    {
        CredentialDisplayItem cdi => cdi.Credential.Sid,
        _ => null
    };

    string? IAppEditDialogSnapshotView.SelectedAppContainerName => _accountComboBox.SelectedItem switch
    {
        AppContainerDisplayItem acdi => acdi.Container.Name,
        _ => null
    };

    string IAppEditDialogSnapshotView.AppPath => _filePathTextBox.Text;
    string IAppEditDialogSnapshotView.AppName => _nameTextBox.Text;
    string IAppEditDialogSnapshotView.DefaultArguments => _defaultArgsTextBox.Text;
    string IAppEditDialogSnapshotView.WorkingDirectory => _workingDirTextBox.Text;
    AclTarget IAppEditDialogSnapshotView.AclTarget => _aclSection.AclTarget;
    AclMode IAppEditDialogSnapshotView.AclMode => _aclSection.AclMode;
    bool IAppEditDialogSnapshotView.IsFolder => _isFolder;
    bool IAppEditDialogSnapshotView.IsUrlScheme => PathHelper.IsUrlScheme(_filePathTextBox.Text);
    PrivilegeLevel? IAppEditDialogSnapshotView.PrivilegeLevel => PrivilegeLevelComboHelper.IndexToMode(_privilegeLevelComboBox.SelectedIndex);
    PrivilegeLevel? IAppEditDialogSnapshotView.PersistedPrivilegeLevel
        => _accountComboBox.SelectedItem is AppContainerDisplayItem
            ? _switchHandler.PriorPrivilegeLevel
            : PrivilegeLevelComboHelper.IndexToMode(_privilegeLevelComboBox.SelectedIndex);
    bool IAppEditDialogSnapshotView.ReplacePrefixes => false;
    bool IAppEditDialogSnapshotView.ManageShortcuts => _manageShortcutsCheckBox.Checked;
    bool IAppEditDialogSnapshotView.RestrictAppEntryAcl => _aclSection.RestrictAcl;
    bool IAppEditDialogSnapshotView.OverrideIpcCallers => _overrideIpcCallersCheckBox.Checked;
    bool IAppEditDialogSnapshotView.AllowPassingArguments => _allowPassArgsCheckBox.Checked;
    bool IAppEditDialogSnapshotView.AllowPassingWorkingDirectory => _allowPassWorkDirCheckBox.Checked;
    string? IAppEditDialogSnapshotView.ArgumentsTemplate => string.IsNullOrEmpty(_argsTemplateTextBox.Text) ? null : _argsTemplateTextBox.Text;
    IReadOnlyList<AppEntry> IAppEditDialogSnapshotView.ExistingApps => [.. _existingApps];
    AppEntry? IAppEditDialogSnapshotView.ExistingApp => _existing;
    string? IAppEditDialogSnapshotView.PreGeneratedId => _preGeneratedId;
    List<string>? IAppEditDialogSnapshotView.IpcCallers => _ipcSection.GetCallers();
    AclConfigSectionSnapshot IAppEditDialogSnapshotView.CaptureAclConfig() => _aclSection.CaptureSnapshot();

    IReadOnlyList<HandlerAssociationItem>? IAppEditDialogSectionsView.GetAssociations() => _associationsSection.GetAssociations();
    void IAppEditDialogSectionsView.SetAssociations(IReadOnlyList<HandlerAssociationItem>? associations)
        => _associationsSection.SetAssociations(associations?.ToList());
    IReadOnlyList<string>? IAppEditDialogSectionsView.GetPathPrefixes() => _appPrefixesSection.GetPrefixes();
    void IAppEditDialogSectionsView.SetPathPrefixes(IReadOnlyList<string>? prefixes) => _appPrefixesSection.SetPrefixes(prefixes);
    Dictionary<string, string>? IAppEditDialogSectionsView.GetEnvironmentVariables() => _envVarsSection.GetItems();
    string? IAppEditDialogSectionsView.GetFirstDuplicateEnvironmentVariableName() => _envVarsSection.GetFirstDuplicateName();
    void IAppEditDialogSectionsView.SetEnvironmentEnabled(bool enabled) => _envVarsSection.SetEnabled(enabled);
    void IAppEditDialogSectionsView.SetAssociationsEnabled(bool enabled) => _associationsSection.SetEnabled(enabled);
    void IAppEditDialogSectionsView.SetPathPrefixesEnabled(bool enabled) => _appPrefixesSection.SetEnabled(enabled);
    void IAppEditDialogSectionsView.SetHandlerContext(string exePath, string? accountSid)
    {
        _associationsSection.ExePath = exePath;
        _associationsSection.AccountSid = accountSid;
    }
    void IAppEditDialogSectionsView.SetPathPrefixTooltip(string? text) => _appPrefixesSection.SetGroupBoxTooltip(_configToolTip!, text);

}
