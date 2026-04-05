namespace RunFence.Wizard.UI.Forms.Steps;

/// <summary>
/// Wizard step for selecting AppContainer capabilities for the Untrusted App (container path) template.
/// None are checked by default — the container is maximally sandboxed.
/// </summary>
public class ContainerCapabilitiesStep : WizardStepPage
{
    private readonly Action<List<string>> _setCapabilities;

    private Label _infoLabel = null!;
    private CheckedListBox _capabilitiesListBox = null!;

    private static readonly (string Sid, string DisplayName)[] KnownCapabilities =
    [
        ("S-1-15-3-1", "Internet Client — outbound internet connections"),
        ("S-1-15-3-2", "Internet Client Server — inbound and outbound internet"),
        ("S-1-15-3-3", "Private Networks — local network access"),
        ("S-1-15-3-7", "Documents Library"),
        ("S-1-15-3-4", "Pictures Library"),
        ("S-1-15-3-5", "Videos Library"),
        ("S-1-15-3-6", "Music Library"),
    ];

    public ContainerCapabilitiesStep(Action<List<string>> setCapabilities)
    {
        _setCapabilities = setCapabilities;
        BuildContent();
    }

    public override string StepTitle => "Container Capabilities";

    public override string? Validate() => null;

    public override void Collect()
    {
        var selected = _capabilitiesListBox.CheckedIndices
            .Cast<int>()
            .Select(i => KnownCapabilities[i].Sid)
            .ToList();
        _setCapabilities(selected);
    }

    private void BuildContent()
    {
        SuspendLayout();
        Padding = new Padding(8);

        _infoLabel = new Label
        {
            Text = "Select which capabilities the AppContainer will have. " +
                   "None are selected by default — the container starts fully sandboxed.",
            AutoSize = false,
            Font = new Font("Segoe UI", 9.5f),
            Dock = DockStyle.Top,
            Padding = new Padding(0, 0, 0, 8)
        };
        TrackWrappingLabel(_infoLabel);

        _capabilitiesListBox = new CheckedListBox
        {
            Font = new Font("Segoe UI", 9.5f),
            Dock = DockStyle.Fill,
            CheckOnClick = true
        };

        foreach (var (_, displayName) in KnownCapabilities)
            _capabilitiesListBox.Items.Add(displayName, false);

        Controls.Add(_capabilitiesListBox);
        Controls.Add(_infoLabel);
        ResumeLayout(false);
    }
}