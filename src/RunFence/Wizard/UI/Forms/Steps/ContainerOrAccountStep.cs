using RunFence.Core.Models;

namespace RunFence.Wizard.UI.Forms.Steps;

/// <summary>
/// Wizard step for choosing between running an untrusted app in an AppContainer or an isolated account.
/// When account is selected, shows Low Integrity and ephemeral options.
/// Uses <see cref="SetBranchStepsProvider"/> so <c>WizardDialog</c> can dynamically insert the appropriate
/// next step (<see cref="ContainerCapabilitiesStep"/> for container, <see cref="FirewallOptionsStep"/> for account).
/// </summary>
public class ContainerOrAccountStep : WizardStepPage
{
    private readonly Action<bool, PrivilegeLevel, bool> _setOptions;

    private RadioButton _accountRadio = null!;
    private RadioButton _containerRadio = null!;
    private CheckBox _lowIntegrityCheckBox = null!;
    private CheckBox _ephemeralCheckBox = null!;
    private Label _accountDescLabel = null!;
    private Label _containerDescLabel = null!;

    public ContainerOrAccountStep(Action<bool, PrivilegeLevel, bool> setOptions)
    {
        _setOptions = setOptions;
        BuildContent();
    }

    /// <summary>Returns true if container mode is currently selected.</summary>
    public bool IsContainerSelected => _containerRadio.Checked;

    public override string StepTitle => "Isolation Mode";

    public override string? Validate() => null;

    public override void Collect()
    {
        var useContainer = _containerRadio.Checked;
        var privilegeLevel = !useContainer && _lowIntegrityCheckBox.Checked
            ? PrivilegeLevel.LowIntegrity
            : PrivilegeLevel.Basic;
        var isEphemeral = _ephemeralCheckBox.Checked;
        _setOptions(useContainer, privilegeLevel, isEphemeral);
    }

    private void BuildContent()
    {
        SuspendLayout();
        Padding = new Padding(8);

        _accountRadio = new RadioButton
        {
            Text = "Isolated account (without Users group)",
            Font = new Font("Segoe UI", 10),
            AutoSize = true,
            Dock = DockStyle.Top,
            Checked = true
        };
        _accountRadio.CheckedChanged += OnRadioChanged;

        _accountDescLabel = new Label
        {
            Text = "Creates a standard Windows account without internet access (configurable). " +
                   "All app launches are managed through RunFence.",
            AutoSize = false,
            Font = new Font("Segoe UI", 9f),
            ForeColor = SystemColors.GrayText,
            Dock = DockStyle.Top,
            Padding = new Padding(20, 0, 0, 8)
        };
        TrackWrappingLabel(_accountDescLabel);

        _lowIntegrityCheckBox = new CheckBox
        {
            Text = "Run at Low Integrity Level",
            Font = new Font("Segoe UI", 9.5f),
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(20, 0, 0, 8)
        };

        _containerRadio = new RadioButton
        {
            Text = "AppContainer (sandboxed)",
            Font = new Font("Segoe UI", 10),
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(0, 8, 0, 0)
        };
        _containerRadio.CheckedChanged += OnRadioChanged;

        _containerDescLabel = new Label
        {
            Text = "Creates a low-privilege AppContainer with tightly controlled capabilities. " +
                   "No network access by default — add capabilities on the next step.",
            AutoSize = false,
            Font = new Font("Segoe UI", 9f),
            ForeColor = SystemColors.GrayText,
            Dock = DockStyle.Top,
            Padding = new Padding(20, 0, 0, 8)
        };
        TrackWrappingLabel(_containerDescLabel);

        _ephemeralCheckBox = new CheckBox
        {
            Text = "Ephemeral account (auto-deleted 24 hours after creation)",
            Font = new Font("Segoe UI", 9.5f),
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(0, 8, 0, 0)
        };

        // Add in reverse order so Dock=Top stacks top-to-bottom
        Controls.Add(_ephemeralCheckBox);
        Controls.Add(_containerDescLabel);
        Controls.Add(_containerRadio);
        Controls.Add(_lowIntegrityCheckBox);
        Controls.Add(_accountDescLabel);
        Controls.Add(_accountRadio);

        ResumeLayout(false);
    }

    /// <summary>
    /// Sets a callback that provides the replacement following-step list when the user switches
    /// between account and container mode. When set, <see cref="OnRadioChanged"/> fires
    /// <see cref="WizardStepPage.ReplaceFollowingSteps"/> automatically so <see cref="WizardDialog"/>
    /// can update the step sequence. The template calls this in <c>CreateSteps</c>.
    /// </summary>
    public void SetBranchStepsProvider(Func<bool, IReadOnlyList<WizardStepPage>> provider)
    {
        _branchStepsProvider = provider;
    }

    private Func<bool, IReadOnlyList<WizardStepPage>>? _branchStepsProvider;

    private void OnRadioChanged(object? sender, EventArgs e)
    {
        // Only act when a radio becomes checked, not when the other one becomes unchecked.
        if (sender is RadioButton { Checked: false })
            return;

        bool isContainer = _containerRadio.Checked;
        _lowIntegrityCheckBox.Visible = !isContainer;
        _ephemeralCheckBox.Text = isContainer
            ? "Ephemeral container (auto-deleted 24 hours after creation)"
            : "Ephemeral account (auto-deleted 24 hours after creation)";

        if (_branchStepsProvider != null)
            RequestReplaceFollowingSteps(_branchStepsProvider(isContainer));
    }
}