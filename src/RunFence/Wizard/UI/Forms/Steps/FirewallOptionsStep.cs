namespace RunFence.Wizard.UI.Forms.Steps;

/// <summary>
/// Wizard step for configuring firewall options (internet, LAN, and localhost access).
/// Uses positive-sense fields matching <c>FirewallAccountSettings</c>: checked = allowed.
/// </summary>
public class FirewallOptionsStep : WizardStepPage
{
    private readonly Action<bool, bool, bool> _setFirewallOptions;

    private Label _infoLabel = null!;
    private CheckBox _allowInternetCheckBox = null!;
    private CheckBox _allowLanCheckBox = null!;
    private CheckBox _allowLocalhostCheckBox = null!;

    public FirewallOptionsStep(
        Action<bool, bool, bool> setFirewallOptions,
        bool defaultInternet = true,
        bool defaultLan = true,
        bool defaultLocalhost = true)
    {
        _setFirewallOptions = setFirewallOptions;
        BuildContent(defaultInternet, defaultLan, defaultLocalhost);
    }

    public override string StepTitle => "Firewall Options";

    public override string? Validate() => null;

    public override void Collect()
    {
        _setFirewallOptions(
            _allowInternetCheckBox.Checked,
            _allowLanCheckBox.Checked,
            _allowLocalhostCheckBox.Checked);
    }

    private void BuildContent(bool defaultInternet, bool defaultLan, bool defaultLocalhost)
    {
        SuspendLayout();
        Padding = new Padding(8);

        _infoLabel = new Label
        {
            Text = "Configure network access for the isolated account. " +
                   "Unchecked items are blocked by Windows Filtering Platform (WFP) firewall rules, " +
                   "preventing the account from making or receiving those connections.",
            AutoSize = false,
            Font = new Font("Segoe UI", 9.5f),
            Dock = DockStyle.Top,
            Padding = new Padding(0, 0, 0, 8)
        };
        TrackWrappingLabel(_infoLabel);

        _allowInternetCheckBox = new CheckBox
        {
            Text = "Allow internet access",
            Font = new Font("Segoe UI", 10),
            AutoSize = true,
            Dock = DockStyle.Top,
            Checked = defaultInternet
        };

        _allowLanCheckBox = new CheckBox
        {
            Text = "Allow LAN (local network) access",
            Font = new Font("Segoe UI", 10),
            AutoSize = true,
            Dock = DockStyle.Top,
            Checked = defaultLan
        };

        _allowLocalhostCheckBox = new CheckBox
        {
            Text = "Allow localhost access",
            Font = new Font("Segoe UI", 10),
            AutoSize = true,
            Dock = DockStyle.Top,
            Checked = defaultLocalhost
        };

        // Reverse order: last added = highest z-order = docks to top first (displays topmost)
        Controls.AddRange(_allowLocalhostCheckBox, _allowLanCheckBox, _allowInternetCheckBox, _infoLabel);
        ResumeLayout(false);
    }
}