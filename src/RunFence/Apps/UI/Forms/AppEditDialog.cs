using RunFence.Account.UI;
using RunFence.Account.UI.AppContainer;
using RunFence.Acl.UI;
using RunFence.Acl.UI.Forms;
using RunFence.Apps;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Persistence;
using RunFence.UI;
using RunFence.UI.Forms;

namespace RunFence.Apps.UI.Forms;

public partial class AppEditDialog : Form, IAppEditDialogState, IAclConfigContextProvider
{
    private ToolTip? _configToolTip;
    private bool _hasLoadedConfigs;
    private bool _isFolder;

    private AppEntry? _existing;
    private List<CredentialEntry> _credentials = null!;
    private List<AppEntry> _existingApps = null!;
    private IReadOnlyDictionary<string, string>? _sidNames;
    private AppDatabase? _database;

    private readonly IAppConfigService _appConfigService;
    private readonly IExeAssociationRegistryReader _reader;
    private readonly IAppEntryIdGenerator _idGenerator;

    private readonly AclConfigSection _aclSection;
    private IpcCallerSection _ipcSection = null!;
    private EnvVarsSection _envVarsSection = null!;
    private HandlerAssociationsSection _associationsSection = null!;
    private readonly AppEditBrowseHelper _browseHelper;
    private readonly AppEditAccountSwitchHandler _switchHandler;
    private readonly AppEditDialogController _controller;
    private readonly AppEditAssociationHandler _associationHandler;
    private readonly AppEditPopulator _populator;
    private readonly AppEditDialogPopulator _appEditDialogPopulator;
    private readonly Func<IpcCallerSection> _ipcCallerSectionFactory;

    public AppEntry Result { get; private set; } = null!;
    public bool ResultReady { get; private set; }
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

    public event Action? ApplyRequested;
    public event Action? RemoveRequested;

    public AppEditDialog(
        IAppConfigService appConfigService,
        AclConfigSection aclSection,
        AppEditBrowseHelper browseHelper,
        AppEditAssociationHandler associationHandler,
        AppEditAccountSwitchHandler switchHandler,
        AppEditDialogController controller,
        AppEditPopulator populator,
        AppEditDialogPopulator appEditDialogPopulator,
        Func<IpcCallerSection> ipcCallerSectionFactory,
        IExeAssociationRegistryReader reader,
        IAppEntryIdGenerator idGenerator)
    {
        _appConfigService = appConfigService;
        _aclSection = aclSection;
        _browseHelper = browseHelper;
        _associationHandler = associationHandler;
        _switchHandler = switchHandler;
        _controller = controller;
        _populator = populator;
        _appEditDialogPopulator = appEditDialogPopulator;
        _ipcCallerSectionFactory = ipcCallerSectionFactory;
        _reader = reader;
        _idGenerator = idGenerator;
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

        _ipcSection = _ipcCallerSectionFactory();
        _ipcSection.SetSidNames(sidNames, database != null ? (sid, name) => database.UpdateSidName(sid, name) : null);

        _switchHandler.Initialize(_privilegeLevelComboBox, _aclSection);
        _controller.Initialize(_switchHandler);

        // Add IpcCallerSection to its container (in Access tab — fixed position above AclSection)
        _ipcSection.Dock = DockStyle.Fill;
        _ipcContainer.Controls.Add(_ipcSection);
        _ipcSection.SetEnabled(false);

        // Add AclConfigSection below the IPC section (fixed y; grows downward into AutoScroll area)
        _aclSection.Location = new Point(0, 160);
        _aclSection.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _tabAccess.Controls.Add(_aclSection);

        // Add EnvVarsSection to Parameters tab
        _envVarsSection = new EnvVarsSection();
        _envVarsSection.Dock = DockStyle.Fill;
        _envVarsContainer.Controls.Add(_envVarsSection);

        // Add HandlerAssociationsSection to Associations tab
        _associationsSection = new HandlerAssociationsSection();
        _associationsSection.Dock = DockStyle.Fill;
        _tabAssociations.Controls.Add(_associationsSection);
        _associationsSection.Changed += OnAssociationsSectionChanged;
        _associationsSection.RegistrySuggestionFactory =
            () => _reader.GetHandledAssociations(_filePathTextBox.Text.Trim());
        _associationsSection.RegistryTemplateLoader =
            key => _reader.GetNonDefaultArguments(_filePathTextBox.Text.Trim(), key);

        // Populate privilege level combobox before account combo (switch handler reads it on selection change)
        _privilegeLevelComboBox.Items.Clear();
        _privilegeLevelComboBox.Items.Add("(Account default)");
        _privilegeLevelComboBox.Items.Add("Highest Allowed");
        _privilegeLevelComboBox.Items.Add("Basic");
        _privilegeLevelComboBox.Items.Add("Low Integrity");
        _privilegeLevelComboBox.SelectedIndex = 0;

        // Populate account combo with credentials and AppContainer items
        _appEditDialogPopulator.PopulateAccountCombo(_accountComboBox, _credentials, _sidNames, _existing, database);

        // Skip the divider if selected
        _accountComboBox.SelectedIndexChanged += OnAccountComboSelectedIndexChanged;

        if (_accountComboBox.Items.Count > 0)
            _accountComboBox.SelectedIndex = 0;

        // Populate config combo
        _appEditDialogPopulator.PopulateConfigCombo(_configComboBox);
        _hasLoadedConfigs = _appConfigService.HasLoadedConfigs;
        _configComboBox.Enabled = _hasLoadedConfigs;
        _configToolTip ??= new ToolTip();
        if (!_hasLoadedConfigs)
            _configToolTip.SetToolTip(_configComboBox, "Load additional configs in Options to save to a different file");
        _configToolTip.SetToolTip(_privilegeLevelComboBox, LaunchUiConstants.PrivilegeLevelTooltip);

        // Runtime-dependent values
        Text = _existing != null ? "Edit Application" : "Add Application";
        _launcherPathTextBox.Text = Path.Combine(AppContext.BaseDirectory, Constants.LauncherExeName);
        _launcherArgsTextBox.Text = _existing != null ? _existing.Id : "(assigned on save)";
        _launcherArgsTextBox.ForeColor = _existing != null ? SystemColors.ControlText : SystemColors.GrayText;
        _removeButton.Visible = _existing != null;

        // Position launch now checkbox relative to OK button
        _launchNowCheckBox.Location = new Point(_okButton.Left - _launchNowCheckBox.PreferredSize.Width - 8, _okButton.Top + 5);

        AcceptButton = _okButton;
        CancelButton = _cancelButton;
        _launchNowCheckBox.Checked = options.LaunchNow;

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
                AppEditDialogPopulator.SelectComboItem<ConfigComboItem>(_configComboBox,
                    item => string.Equals(item.Path, options.ConfigPath, StringComparison.OrdinalIgnoreCase),
                    startIndex: 1);

            if (options.ExePath != null)
            {
                _filePathTextBox.Text = options.ExePath;
                _nameTextBox.Text = Path.GetFileNameWithoutExtension(options.ExePath);
            }

            if (options.AccountSid != null)
            {
                AppEditDialogPopulator.SelectComboItem<CredentialDisplayItem>(_accountComboBox,
                    ci => string.Equals(ci.Credential.Sid, options.AccountSid, StringComparison.OrdinalIgnoreCase));
            }
            else if (options.ContainerName != null)
            {
                AppEditDialogPopulator.SelectComboItem<AppContainerDisplayItem>(_accountComboBox,
                    acdi => string.Equals(acdi.Container.Name, options.ContainerName, StringComparison.OrdinalIgnoreCase));
            }

            _aclSection.RestrictAcl = options.RestrictAcl;
            _manageShortcutsCheckBox.Checked = options.ManageShortcuts;
            _privilegeLevelComboBox.SelectedIndex = PrivilegeLevelComboHelper.ModeToIndex(options.PrivilegeLevel);

            // Populate associations section for new app (uses pre-generated ID)
            PopulateAssociationsSection(_preGeneratedId);
        }
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

    private string? _preGeneratedId;

    private void PreGenerateId()
    {
        _preGeneratedId = _idGenerator.GenerateUniqueId(_existingApps.Select(a => a.Id));
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
    }

    private void OnBrowseClick(object? sender, EventArgs e)
    {
        var path = _browseHelper.BrowseFile();
        if (path != null)
        {
            _filePathTextBox.Text = path;
            if (string.IsNullOrWhiteSpace(_nameTextBox.Text))
                _nameTextBox.Text = Path.GetFileNameWithoutExtension(path);
            SetFolderMode(false);
        }
    }

    private void OnBrowseFolderClick(object? sender, EventArgs e)
    {
        var path = _browseHelper.BrowseFolder();
        if (path != null)
        {
            _filePathTextBox.Text = path;
            if (string.IsNullOrWhiteSpace(_nameTextBox.Text))
                _nameTextBox.Text = Path.GetFileName(path);
            SetFolderMode(true);
        }
    }

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
            var apps = await Task.Run(() => _browseHelper.DiscoverApps());

            if (IsDisposed)
                return;

            using var dlg = new AppDiscoveryDialog(apps);
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _filePathTextBox.Text = dlg.SelectedPath;
                if (string.IsNullOrWhiteSpace(_nameTextBox.Text) && dlg.SelectedName != null)
                    _nameTextBox.Text = dlg.SelectedName;
                SetFolderMode(false);
            }
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

    private void OnRemoveClick(object? sender, EventArgs e)
    {
        if (_existing == null)
            return;
        var removeMessage = AppEntryHelper.GetRemoveConfirmationMessage(_existing);
        if (MessageBox.Show(removeMessage, "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        RemoveRequested?.Invoke();
    }

    private void OnIpcOverrideChanged(object? sender, EventArgs e)
    {
        _ipcSection.SetEnabled(_overrideIpcCallersCheckBox.Checked);
    }

    private void PopulateAssociationsSection(string? appId)
    {
        if (_database == null || string.IsNullOrEmpty(appId))
            return;
        var associations = _associationHandler.GetCurrentAssociations(appId);
        _associationsSection.SetAssociations(associations);
    }

    private void PopulateFromExisting()
    {
        var app = _existing!;

        // Phase 1: Load data from app entry into state record (also populates sections)
        var state = _populator.LoadExistingApp(app, _aclSection, _ipcSection, _envVarsSection);

        _nameTextBox.Text = state.Name;
        _isFolder = state.IsFolder;
        if (_isFolder)
            _filePathLabel.Text = "Folder Path:";

        _filePathTextBox.Text = state.ExePath;
        _filePathTextBox.ReadOnly = true;
        _filePathTextBox.BackColor = SystemColors.Control;
        _browseButton.Visible = false;
        _browseFolderButton.Visible = false;
        _discoverButton.Visible = false;
        _defaultArgsTextBox.Text = state.DefaultArguments;
        _allowPassArgsCheckBox.Checked = state.AllowPassingArguments;
        _argsTemplateTextBox.Text = state.ArgumentsTemplate;
        _workingDirTextBox.Text = state.WorkingDirectory;
        _allowPassWorkDirCheckBox.Checked = state.AllowPassingWorkingDirectory;

        _manageShortcutsCheckBox.Checked = state.ManageShortcuts;
        _privilegeLevelComboBox.SelectedIndex = PrivilegeLevelComboHelper.ModeToIndex(state.SelectedPrivilegeLevel);
        _overrideIpcCallersCheckBox.Checked = state.OverrideIpcCallers;

        // Phase 2: Account combo selection — MUST come after PrivilegeLevel combobox is set,
        // because the switch handler captures its state as "prior" when a container is selected.
        _controller.SelectAccountComboForExisting(app, _sidNames, _accountComboBox);

        // Phase 3: ACL path + config combo
        _controller.SelectConfigAndAclPath(app, _appConfigService, _aclSection, _configComboBox, _hasLoadedConfigs);

        // Phase 4: Handler associations
        PopulateAssociationsSection(app.Id);

        UpdateFolderState();
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        var result = _controller.ValidateAndBuild(
            this, _aclSection, _ipcSection, _envVarsSection,
            _existingApps, _existing, _preGeneratedId);

        if (result == null)
            return;

        Result = result;
        ResultReady = true;

        // Pre-assign the app to its config so SetHandlerMapping writes to the correct config file.
        // ApplyRequested will call AssignApp again (idempotent) before saving.
        if (_database != null)
            _appConfigService.AssignApp(result.Id, SelectedConfigPath);

        if (ApplyRequested != null)
        {
            Enabled = false;
            _statusLabel.ForeColor = SystemColors.ControlText;
            _statusLabel.Text = "Applying ACLs and shortcuts...";
            // Snapshot original associations before applying changes so we can roll back on failure.
            IReadOnlyList<HandlerAssociationItem> originalAssociations = _database != null
                ? _associationHandler.GetCurrentAssociations(result.Id) ?? []
                : [];
            bool saved = false;
            try
            {
                // Apply in-memory handler mapping changes before invoking ApplyRequested so they
                // are included in the save. Registry sync is deferred until after save succeeds to
                // avoid a diverged registry state if the save throws.
                if (_database != null)
                    _associationHandler.ApplyChanges(result.Id, _associationsSection.GetAssociations() ?? []);
                ApplyRequested.Invoke();
                saved = true;
            }
            catch (Exception ex)
            {
                // Roll back in-memory association changes if save failed
                if (_database != null)
                    _associationHandler.RevertChanges(result.Id, originalAssociations);
                Enabled = true;
                _statusLabel.ForeColor = Color.Red;
                _statusLabel.Text = $"Failed: {ex.Message}";
            }
            // Sync registry only after the save has succeeded; runs outside the save catch so
            // registry errors never trigger a rollback of already-persisted DB changes.
            if (saved && _database != null)
            {
                try
                {
                    _associationHandler.SyncRegistry();
                }
                catch (Exception ex)
                {
                    // Registry sync is best-effort post-save; log to status bar but do not roll back.
                    _statusLabel.ForeColor = Color.DarkOrange;
                    _statusLabel.Text = $"Saved, but handler sync failed: {ex.Message}";
                }
            }
        }
        else
        {
            if (_database != null)
                _associationHandler.ApplyAndSync(result.Id, _associationsSection.GetAssociations() ?? []);

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}