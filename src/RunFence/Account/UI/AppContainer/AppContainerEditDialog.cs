using RunFence.Apps.UI;
using RunFence.Core.Models;
using RunFence.UI;
using RunFence.UI.Forms;

namespace RunFence.Account.UI.AppContainer;

/// <summary>
/// Dialog for creating or editing an AppContainer entry.
/// </summary>
public partial class AppContainerEditDialog : ContextHelpForm, IAppContainerEditDialogResultContext
{
    private readonly AppContainerEditSubmitController _submitController;
    private readonly AppContainerDialogStateAssembler _stateAssembler;
    private readonly AppContainerCapabilitiesBinder _capabilitiesBinder;
    private readonly AppContainerDialogResultPresenter _resultPresenter;
    private AppContainerEntry? _existing;
    private Panel _contextHelpHost = null!;
    private ContextHelpButton _contextHelpButton = null!;
    private Panel _contextHelpTopRow = null!;

    private const int SidRowIndex = 2;
    private const int EphemeralRowIndex = 5;

    /// <summary>The newly created AppContainerEntry, or null if editing an existing one.</summary>
    public AppContainerEntry? CreatedEntry { get; private set; }

    public bool DeleteRequested { get; private set; }

    public AppContainerOperationStatus? LastOperationStatus { get; private set; }

    private string PendingValidationCaption { get; set; } = "Validation";

    private AppContainerOperationStatus? PendingNotificationStatus { get; set; }

    bool IAppContainerEditDialogNotificationContext.IsCreateMode => _existing == null;

    string IAppContainerEditDialogNotificationContext.PendingValidationCaption => PendingValidationCaption;

    AppContainerOperationStatus? IAppContainerEditDialogNotificationContext.PendingNotificationStatus => PendingNotificationStatus;

    AppContainerEntry? IAppContainerEditDialogResultContext.CreatedEntry
    {
        get => CreatedEntry;
        set => CreatedEntry = value;
    }

    AppContainerOperationStatus? IAppContainerEditDialogResultContext.LastOperationStatus
    {
        get => LastOperationStatus;
        set => LastOperationStatus = value;
    }

    AppContainerOperationStatus? IAppContainerEditDialogResultContext.PendingNotificationStatus
    {
        get => PendingNotificationStatus;
        set => PendingNotificationStatus = value;
    }

    public AppContainerEditDialog(
        AppContainerEditSubmitController submitController,
        AppContainerDialogStateAssembler stateAssembler,
        AppContainerCapabilitiesBinder capabilitiesBinder,
        AppContainerDialogResultPresenter resultPresenter)
    {
        _submitController = submitController;
        _stateAssembler = stateAssembler;
        _capabilitiesBinder = capabilitiesBinder;
        _resultPresenter = resultPresenter;

        InitializeComponent();
        BuildDynamicContent();
    }

    public void Initialize(AppContainerEntry? existing)
    {
        _existing = existing;
        PendingValidationCaption = "Validation";
        PendingNotificationStatus = null;
        LastOperationStatus = null;
        CreatedEntry = null;
        DeleteRequested = false;
        _comCustomListBox.Items.Clear();

        ConfigureMode();
        if (_existing != null)
        {
            _capabilitiesBinder.PopulateFromExisting(
                _existing,
                _displayNameBox,
                _profileNameBox,
                _sidBox,
                _capCheckBoxes,
                _loopbackCheckBox,
                _ephemeralCheckBox,
                _comCustomListBox);
        }
        else
        {
            _displayNameBox.Text = string.Empty;
            _profileNameBox.Text = string.Empty;
            _sidBox.Text = string.Empty;
            _ephemeralCheckBox.Checked = false;
            _capabilitiesBinder.ApplyDefaultCapabilities(_capCheckBoxes, _loopbackCheckBox);
        }

        _capabilitiesBinder.RefreshProfileNamePreview(_existing, _displayNameBox, _profileNameBox, _ephemeralCheckBox);
    }

    private void BuildDynamicContent()
    {
        EnsureContextHelpTopRow();
        Icon = AppIcons.GetAppIcon();
        _capCheckBoxes = _capabilitiesBinder.InitializeCapabilityRows(_capFlow, _loopbackCheckBox);
        _capabilitiesBinder.WireComToolbar(
            _comToolStrip,
            _comCustomListBox,
            components,
            this,
            caption => PendingValidationCaption = caption);
        RegisterContextHelp();
    }

    private void EnsureContextHelpTopRow()
    {
        if (_contextHelpHost != null)
            return;

        var rowHeight = ScaleHelpLogicalPixels(33);
        var buttonSize = ScaleHelpLogicalPixels(29);
        var verticalPadding = Math.Max(0, ScaleHelpLogicalPixels(2));

        _contextHelpTopRow = new Panel
        {
            BackColor = SystemColors.Control,
            Dock = DockStyle.Top,
            Height = rowHeight,
            Padding = new Padding(0, verticalPadding, 0, verticalPadding),
            TabStop = false
        };

        _contextHelpHost = new Panel
        {
            BackColor = SystemColors.Control,
            Dock = DockStyle.Right,
            Padding = Padding.Empty,
            Size = new Size(buttonSize, buttonSize),
            TabStop = false
        };

        _contextHelpButton = new ContextHelpButton
        {
            Name = "_contextHelpButton",
            AccessibleName = "Context help",
            Dock = DockStyle.Right,
            Size = new Size(buttonSize, buttonSize),
            TabStop = false
        };

        _contextHelpHost.Controls.Add(_contextHelpButton);
        _contextHelpTopRow.Controls.Add(_contextHelpHost);
        Controls.Add(_contextHelpTopRow);
        ClientSize = new Size(ClientSize.Width, ClientSize.Height + rowHeight);
    }

    private void RegisterContextHelp()
    {
        SetContextHelp(_contextHelpButton, ContextHelpTextResolver.InstructionText);
        SetContextHelp(_comGroupBox, ContextHelpTextCatalog.AppContainer_ComAccess);
        SetContextHelp(_ephemeralCheckBox, ContextHelpTextCatalog.EphemeralIdentity);
    }

    private void ShowTableRow(int rowIndex, bool visible)
    {
        _layout.RowStyles[rowIndex].SizeType = visible ? SizeType.AutoSize : SizeType.Absolute;
        _layout.RowStyles[rowIndex].Height = 0;
        foreach (Control control in _layout.Controls)
        {
            if (_layout.GetRow(control) == rowIndex)
                control.Visible = visible;
        }
    }

    private void ConfigureMode()
    {
        var isEdit = _existing != null;
        Text = isEdit ? "Edit App Container" : "Create App Container";
        ShowTableRow(SidRowIndex, isEdit);
        ShowTableRow(EphemeralRowIndex, true);
        _deleteButton.Visible = isEdit;
        _profileNameBox.ReadOnly = isEdit;
        _profileNameBox.BackColor = isEdit ? SystemColors.Control : SystemColors.Window;
        _ephemeralCheckBox.Enabled = !isEdit;
        if (isEdit)
        {
            _toolTip.SetToolTip(_profileNameBox, "Cannot be changed — determines the container SID.");
            _toolTip.SetToolTip(_sidBox, "Select and copy with Ctrl+C.");
            _sidBox.Text = !string.IsNullOrEmpty(_existing!.Sid) ? _existing.Sid : "(unavailable)";
        }
    }

    private void OnDisplayNameChanged(object? sender, EventArgs e)
    {
        _capabilitiesBinder.RefreshProfileNamePreview(_existing, _displayNameBox, _profileNameBox, _ephemeralCheckBox);
    }

    private void OnEphemeralChanged(object? sender, EventArgs e)
    {
        _capabilitiesBinder.RefreshProfileNamePreview(_existing, _displayNameBox, _profileNameBox, _ephemeralCheckBox);
    }

    private void SetBusy(bool busy)
    {
        UseWaitCursor = busy;
        _displayNameBox.Enabled = !busy;
        _profileNameBox.Enabled = !busy && _existing == null;
        _sidBox.Enabled = !busy;
        _capGroupBox.Enabled = !busy;
        _comGroupBox.Enabled = !busy;
        _ephemeralCheckBox.Enabled = !busy && _existing == null;
        _okButton.Enabled = !busy;
        _cancelButton.Enabled = !busy;
        _deleteButton.Enabled = !busy;
    }

    private async void OnOkClick(object? sender, EventArgs e)
    {
        await SubmitAsync();
    }

    private async Task SubmitAsync()
    {
        PendingValidationCaption = "Validation";
        DialogResult? closeResult = null;
        SetBusy(true);

        try
        {
            var submitRequest = _stateAssembler.BuildRequest(
                _existing,
                _displayNameBox.Text,
                _ephemeralCheckBox.Checked,
                _capCheckBoxes
                    .Where(checkBox => checkBox.Checked)
                    .Select(checkBox => (string)checkBox.Tag!)
                    .ToList(),
                _loopbackCheckBox.Checked,
                _comCustomListBox.Items.Cast<string>().ToList());
            var submitResult = await _submitController.SubmitAsync(submitRequest);
            if (IsDisposed)
                return;

            closeResult = _resultPresenter.ApplyResult(this, this, submitResult);
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
                _resultPresenter.ApplyUnhandledException(this, ex);
        }
        finally
        {
            if (!IsDisposed && closeResult == null)
                SetBusy(false);
        }

        if (!IsDisposed && closeResult != null)
        {
            DialogResult = closeResult.Value;
            Close();
        }
    }

    private void OnDeleteClick(object? sender, EventArgs e)
    {
        DeleteRequested = true;
        DialogResult = DialogResult.Cancel;
        Close();
    }
}
