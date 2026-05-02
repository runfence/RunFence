using RunFence.Account.UI;
using RunFence.Account.UI.AppContainer;
using RunFence.Acl.UI;
using RunFence.Acl.UI.Forms;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Core.Models;
using RunFence.Launching.Resolution;
using RunFence.Persistence;
using RunFence.UI;
using RunFence.UI.Forms;

namespace RunFence.Apps.UI.Forms;

public partial class AppEditDialog : Form, IAppEditDialogState, IAclConfigContextProvider, IAppEditBrowseResultReceiver
{
    private ToolTip? _configToolTip;
    private bool _hasLoadedConfigs;
    private bool _isFolder;
    private string? _lastAboveBasicPromptedPath;

    private AppEntry? _existing;
    private List<CredentialEntry> _credentials = null!;
    private List<AppEntry> _existingApps = null!;
    private IReadOnlyDictionary<string, string>? _sidNames;
    private AppDatabase? _database;

    private readonly IAppConfigService _appConfigService;

    private readonly AclConfigSection _aclSection;
    private IpcCallerSection _ipcSection = null!;
    private EnvVarsSection _envVarsSection = null!;
    private readonly HandlerAssociationsSection _associationsSection;
    private PathPrefixesSection _appPrefixesSection = null!;
    private readonly AppEditBrowseHelper _browseHelper;
    private readonly AppEditAccountSwitchHandler _switchHandler;
    private readonly AppEditDialogController _controller;
    private readonly IExecutablePathResolver _executablePathResolver;
    private readonly ISidResolver _sidResolver;
    private readonly IProfilePathResolver _profilePathResolver;
    private readonly AppEditAssociationHandler _associationHandler;
    private readonly AppEditDialogSaveHandler _saveHandler;
    private readonly AppEditDialogPopulator _populator;
    private readonly AppEditDialogInitializer _initializer;
    private readonly Func<IpcCallerSection> _ipcCallerSectionFactory;

    public AppEntry Result { get; private set; } = null!;
    public bool LaunchNow => _launchNowCheckBox.Checked;

    /// <summary>Returns null for main config, or the full path for an additional config.</summary>
    public string? SelectedConfigPath
    {
        get
        {
            if (_hasLoadedConfigs)
                return _configComboBox.SelectedItem is ConfigComboItem item ? item.Path : null;

            // Combo disabled (no additional configs loaded) — preserve the existing app's current config
            // assignment to prevent silently demoting it to main config on edit.
            return _existing != null ? _appConfigService.GetConfigPath(_existing.Id) : null;
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

    // IAppEditDialogState implementation
    string IAppEditDialogState.NameText => _nameTextBox.Text;
    string IAppEditDialogState.FilePathText => _filePathTextBox.Text;
    bool IAppEditDialogState.IsFolder => _isFolder;
    object? IAppEditDialogState.SelectedAccountItem => _accountComboBox.SelectedItem;
    bool IAppEditDialogState.ManageShortcuts => _manageShortcutsCheckBox.Checked;
    PrivilegeLevel? IAppEditDialogState.SelectedPrivilegeLevel =>
        PrivilegeLevelComboHelper.IndexToMode(_privilegeLevelComboBox.SelectedIndex);
    bool IAppEditDialogState.OverrideIpcCallers => _overrideIpcCallersCheckBox.Checked;
    string IAppEditDialogState.DefaultArgsText => _defaultArgsTextBox.Text;
    bool IAppEditDialogState.AllowPassArgs => _allowPassArgsCheckBox.Checked;
    string IAppEditDialogState.WorkingDirText => _workingDirTextBox.Text;
    bool IAppEditDialogState.AllowPassWorkDir => _allowPassWorkDirCheckBox.Checked;

    string IAppEditDialogState.StatusText
    {
        set => _statusLabel.Text = value;
    }

    string? IAppEditDialogState.ArgumentsTemplateText => string.IsNullOrEmpty(_argsTemplateTextBox.Text) ? null : _argsTemplateTextBox.Text;

    IReadOnlyList<string>? IAppEditDialogState.AppPathPrefixes => _appPrefixesSection.GetPrefixes();

    public event Func<Task>? ApplyRequested;
    public event Func<Task>? RemoveRequested;

    internal AppEditDialog(
        IAppConfigService appConfigService,
        AclConfigSection aclSection,
        AppEditBrowseHelper browseHelper,
        AppEditAssociationHandler associationHandler,
        AppEditDialogSaveHandler saveHandler,
        AppEditAccountSwitchHandler switchHandler,
        AppEditDialogController controller,
        IExecutablePathResolver executablePathResolver,
        ISidResolver sidResolver,
        IProfilePathResolver profilePathResolver,
        AppEditDialogPopulator populator,
        AppEditDialogInitializer initializer,
        HandlerAssociationsSection associationsSection,
        Func<IpcCallerSection> ipcCallerSectionFactory)
    {
        _appConfigService = appConfigService;
        _aclSection = aclSection;
        _browseHelper = browseHelper;
        _associationHandler = associationHandler;
        _saveHandler = saveHandler;
        _switchHandler = switchHandler;
        _controller = controller;
        _executablePathResolver = executablePathResolver;
        _sidResolver = sidResolver;
        _profilePathResolver = profilePathResolver;
        _populator = populator;
        _initializer = initializer;
        _associationsSection = associationsSection;
        _ipcCallerSectionFactory = ipcCallerSectionFactory;
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
        AppEditDialogOptions? options = null,
        IReadOnlyDictionary<string, string>? sidNames = null,
        AppDatabase? database = null)
    {
        options ??= new AppEditDialogOptions();

        _existing = existing;
        _credentials = credentials;
        _existingApps = existingApps;
        _sidNames = sidNames;
        _database = database;

        _ipcSection = CreateIpcSection(sidNames, database);

        _switchHandler.Initialize(_privilegeLevelComboBox, _aclSection);
        _controller.Initialize(_switchHandler);

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
        _associationsSection.Dock = DockStyle.Fill;
        _appPrefixesSection.Dock = DockStyle.Fill;
        var assocLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
        assocLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        assocLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        assocLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        assocLayout.Controls.Add(_associationsSection, 0, 0);
        assocLayout.Controls.Add(_appPrefixesSection, 0, 1);
        _tabAssociations.Controls.Add(assocLayout);
        _associationsSection.Changed += OnAssociationsSectionChanged;

        // Populate privilege level combobox before account combo (switch handler reads it on selection change)
        _privilegeLevelComboBox.Items.Clear();
        _privilegeLevelComboBox.Items.Add("(Account default)");
        _privilegeLevelComboBox.Items.Add("Highest Allowed");
        _privilegeLevelComboBox.Items.Add("Above Basic");
        _privilegeLevelComboBox.Items.Add("Basic");
        _privilegeLevelComboBox.Items.Add("Low Integrity");
        _privilegeLevelComboBox.SelectedIndex = 0;

        // Populate account and config combo boxes
        PopulateAccountCombo(database);
        PopulateConfigCombo();

        // Skip the divider if selected
        _accountComboBox.SelectedIndexChanged += OnAccountComboSelectedIndexChanged;

        if (_accountComboBox.Items.Count > 0)
            _accountComboBox.SelectedIndex = 0;

        _hasLoadedConfigs = _appConfigService.HasLoadedConfigs;
        _configComboBox.Enabled = _hasLoadedConfigs;
        _configToolTip ??= new ToolTip();
        if (!_hasLoadedConfigs)
            _configToolTip.SetToolTip(_configComboBox, "Load additional configs in Options to save to a different file");
        _configToolTip.SetToolTip(_privilegeLevelComboBox, LaunchUiConstants.PrivilegeLevelTooltip);

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

        if (_existing != null)
            PopulateFromExisting();
        else
        {
            PreGenerateId();
            if (options.ConfigPath != null && _hasLoadedConfigs)
                SelectComboItem<ConfigComboItem>(_configComboBox,
                    item => string.Equals(item.Path, options.ConfigPath, StringComparison.OrdinalIgnoreCase),
                    startIndex: 1);

            if (options.ExePath != null)
            {
                _filePathTextBox.Text = options.ExePath;
                _nameTextBox.Text = Path.GetFileNameWithoutExtension(options.ExePath);
            }

            if (options.AccountSid != null)
            {
                SelectComboItem<CredentialDisplayItem>(_accountComboBox,
                    ci => SidComparer.SidEquals(ci.Credential.Sid, options.AccountSid));
            }
            else if (options.ContainerName != null)
            {
                SelectComboItem<AppContainerDisplayItem>(_accountComboBox,
                    acdi => string.Equals(acdi.Container.Name, options.ContainerName, StringComparison.OrdinalIgnoreCase));
            }

            _aclSection.RestrictAcl = options.RestrictAcl;
            _manageShortcutsCheckBox.Checked = options.ManageShortcuts;
            _privilegeLevelComboBox.SelectedIndex = PrivilegeLevelComboHelper.ModeToIndex(options.PrivilegeLevel);

            // Populate associations section for new app (uses pre-generated ID)
            _associationsSection.SetAssociations(_initializer.GetAssociations(_database, _preGeneratedId)?.ToList());
        }
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

        _switchHandler.HandleSelectionChanged(_accountComboBox.SelectedItem);
    }

    private void OnAssociationsSectionChanged()
    {
        // Auto-enable AllowPassingArguments when associations are added
        if (_associationsSection.GetAssociations()?.Count > 0)
            _allowPassArgsCheckBox.Checked = true;
    }

    private void PopulateAccountCombo(AppDatabase? database)
    {
        _accountComboBox.Items.Clear();
        foreach (var item in _populator.BuildAccountItems(_credentials, _sidNames, _existing, database))
            _accountComboBox.Items.Add(item);
    }

    private void PopulateConfigCombo()
    {
        _configComboBox.Items.Clear();
        foreach (var item in _populator.BuildConfigItems())
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

    private void PreGenerateId()
    {
        _preGeneratedId = _initializer.PreGenerateId(_existingApps.Select(a => a.Id));
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
        _associationsSection.ExePath = text.Trim();
        UpdateFolderState();
    }

    private void SetFolderMode(bool isFolder)
    {
        _isFolder = isFolder;
        _filePathLabel.Text = isFolder ? "Folder Path:" : "File Path or URL:";
        _aclSection.SetExePath(_filePathTextBox.Text, _isFolder);
        UpdateFolderState();
    }

    private void UpdateFolderState()
    {
        var isUrl = PathHelper.IsUrlScheme(_filePathTextBox.Text);
        _defaultArgsTextBox.Enabled = !isUrl && !_isFolder;
        _allowPassArgsCheckBox.Enabled = !isUrl && !_isFolder;
        _argsTemplateTextBox.Enabled = !isUrl && !_isFolder;
        _workingDirTextBox.Enabled = !isUrl && !_isFolder;
        _workingDirBrowseButton.Enabled = !isUrl && !_isFolder;
        _allowPassWorkDirCheckBox.Enabled = !isUrl && !_isFolder;
        _envVarsSection.SetEnabled(!isUrl && !_isFolder);
        _associationsSection.SetEnabled(!isUrl && !_isFolder);
        _appPrefixesSection.SetEnabled(!_isFolder);

        if (_configToolTip != null)
            _appPrefixesSection.SetGroupBoxTooltip(_configToolTip,
                isUrl ? "URL-scheme prefixes filter which URLs this app handles." : null);
    }

    // IAppEditBrowseResultReceiver implementation
    string IAppEditBrowseResultReceiver.GetAppName() => _nameTextBox.Text;
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

    bool IAppEditBrowseResultReceiver.CanSuggestAboveBasicPrivilegeLevel()
    {
        if (!_privilegeLevelComboBox.Enabled)
            return false;
        var selected = PrivilegeLevelComboHelper.IndexToMode(_privilegeLevelComboBox.SelectedIndex);
        if (selected is PrivilegeLevel.HighestAllowed or PrivilegeLevel.AboveBasic)
            return false;
        // "(Account default)" — also check what the account default resolves to
        if (selected == null)
        {
            var sid = (_accountComboBox.SelectedItem as CredentialDisplayItem)?.Credential.Sid;
            var accountDefault = sid != null ? _database?.GetAccount(sid)?.PrivilegeLevel : null;
            if (accountDefault is PrivilegeLevel.HighestAllowed or PrivilegeLevel.AboveBasic)
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
        Cursor = Cursors.WaitCursor;
        _discoverButton.Enabled = false;
        try
        {
            if (!IsDisposed)
                await _browseHelper.DiscoverAndApplyAsync(this);
        }
        finally
        {
            if (!IsDisposed)
            {
                Cursor = Cursors.Default;
                _discoverButton.Enabled = true;
            }
        }
    }

    private async void OnRemoveClick(object? sender, EventArgs e)
    {
        if (_existing == null)
            return;
        var removeMessage = AppEntryHelper.GetRemoveConfirmationMessage(_existing);
        if (MessageBox.Show(removeMessage, "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        if (RemoveRequested != null)
            await RemoveRequested.Invoke();
    }

    private void OnIpcOverrideChanged(object? sender, EventArgs e)
    {
        _ipcSection.SetEnabled(_overrideIpcCallersCheckBox.Checked);
    }

    private IpcCallerSection CreateIpcSection(IReadOnlyDictionary<string, string>? sidNames, AppDatabase? database)
    {
        var ipcSection = _ipcCallerSectionFactory();
        ipcSection.SetSidNames(sidNames, database != null ? (sid, name) => database.UpdateSidName(sid, name) : null);
        return ipcSection;
    }

    private void ApplyExistingInitialization(AppEditInitializationModel model)
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

        SelectAccountComboForExisting(model.AccountSelection);
        SelectConfigAndAclPath(model);
        _associationsSection.SetAssociations(model.Associations?.ToList());
        _appPrefixesSection.SetPrefixes(model.PathPrefixes);
        UpdateFolderState();
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
        _filePathTextBox.ReadOnly = true;
        _filePathTextBox.BackColor = SystemColors.Control;
        _browseButton.Visible = false;
        _browseFolderButton.Visible = false;
        _discoverButton.Visible = false;
        _filePathTextBox.Width = _nameTextBox.Width;
    }

    private void SelectAccountComboForExisting(AppEditExistingAccountSelection selection)
    {
        _accountComboBox.SelectedIndex = -1;

        if (selection.AppContainerName != null)
        {
            SelectComboItem<AppContainerDisplayItem>(_accountComboBox,
                ci => string.Equals(ci.Container.Name, selection.AppContainerName, StringComparison.OrdinalIgnoreCase));
            return;
        }

        var found = SelectComboItem<CredentialDisplayItem>(_accountComboBox,
            item => SidComparer.SidEquals(item.Credential.Sid, selection.AccountSid));

        if (found)
            return;

        var fallbackEntry = new CredentialEntry { Sid = selection.AccountSid };
        var fallbackItem = new CredentialDisplayItem(fallbackEntry, _sidResolver, _profilePathResolver, _sidNames);
        _accountComboBox.Items.Add(fallbackItem);
        _accountComboBox.SelectedItem = fallbackItem;
    }

    private void SelectConfigAndAclPath(AppEditInitializationModel model)
    {
        // SetExePath rebuilds the folder depth combo and updates ACL state.
        // SelectFolderDepth must come after to override the default depth.
        _aclSection.SetExePath(model.State.ExePath, model.State.IsFolder);
        _aclSection.SelectFolderDepth(model.AclState.FolderAclDepth);

        if (!_hasLoadedConfigs)
            return;

        _configComboBox.SelectedIndex = 0;
        if (model.SelectedConfigPath != null)
            SelectComboItem<ConfigComboItem>(_configComboBox,
                item => string.Equals(item.Path, model.SelectedConfigPath, StringComparison.OrdinalIgnoreCase),
                startIndex: 1);
    }

    private void PopulateFromExisting()
        => ApplyExistingInitialization(_initializer.CreateExistingInitializationModel(_existing!, _database));

    private void TryPromptAboveBasicForCurrentPath()
    {
        if (_existing != null || _isFolder)
            return;
        var path = _filePathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path) ||
            string.Equals(path, _lastAboveBasicPromptedPath, StringComparison.OrdinalIgnoreCase))
            return;
        _lastAboveBasicPromptedPath = path;
        var sid = (_accountComboBox.SelectedItem as CredentialDisplayItem)?.Credential.Sid;
        var resolvedPath = _executablePathResolver.TryResolvePath(
            path,
            ExecutablePathResolutionContext.CurrentProcess(sid));
        _browseHelper.PromptAboveBasicIfNeeded(resolvedPath ?? path, this);
    }

    private async void OnOkClick(object? sender, EventArgs e)
    {
        TryPromptAboveBasicForCurrentPath();

        var result = _controller.ValidateAndBuild(
            this, _aclSection, _ipcSection, _envVarsSection,
            _existingApps, _existing, _preGeneratedId);

        if (result == null)
            return;

        Result = result;
        if (ApplyRequested != null)
        {
            Enabled = false;
            _statusLabel.ForeColor = SystemColors.ControlText;
            _statusLabel.Text = "Applying ACLs and shortcuts...";

            // Apply in-memory handler mapping changes before invoking ApplyRequested so they
            // are included in the save. Registry sync is deferred until after save succeeds to
            // avoid a diverged registry state if the save throws.
            var currentAssociations = _associationsSection.GetAssociations() ?? [];
            await _saveHandler.TrySaveAndApply(
                result,
                SelectedConfigPath,
                _database,
                currentAssociations,
                ApplyRequested.Invoke,
                errorMessage =>
                {
                    Enabled = true;
                    _statusLabel.ForeColor = Color.Red;
                    _statusLabel.Text = $"Failed: {errorMessage}";
                },
                // Registry sync is best-effort post-save; log to status bar but do not roll back.
                warningMessage =>
                {
                    _statusLabel.ForeColor = Color.DarkOrange;
                    _statusLabel.Text = $"Saved, but handler sync failed: {warningMessage}";
                });
        }
        else
        {
            if (_database != null)
            {
                // Pre-assign the app to its config so SetHandlerMapping writes to the correct config file.
                _appConfigService.AssignApp(result.Id, SelectedConfigPath);
                _associationHandler.ApplyAndSync(result.Id, _associationsSection.GetAssociations() ?? []);
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
