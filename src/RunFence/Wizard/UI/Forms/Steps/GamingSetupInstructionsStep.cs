namespace RunFence.Wizard.UI.Forms.Steps;

/// <summary>
/// Wizard step shown after account selection in the Gaming Account template.
/// Instructs the user on launcher installation and game folder preparation.
/// Content varies depending on whether a new account is being created or an existing one is used.
/// </summary>
public class GamingSetupInstructionsStep : WizardStepPage
{
    /// <param name="isCreateNew">
    /// True when the user chose "Create new account" — instructions recommend installing launchers for all users.
    /// False when an existing account was selected — instructions suggest per-user install if possible.
    /// </param>
    public GamingSetupInstructionsStep(bool isCreateNew)
    {
        BuildContent(isCreateNew);
    }

    public override string StepTitle => "Before You Begin";

    public override string? Validate() => null;

    public override void Collect()
    {
    }

    private void BuildContent(bool isCreateNew)
    {
        SuspendLayout();
        Padding = new Padding(8);

        var titleLabel = new Label
        {
            Text = "Prepare your game launchers",
            AutoSize = false,
            Font = new Font("Segoe UI Semibold", 11f),
            ForeColor = Color.FromArgb(0x1A, 0x1A, 0x1A),
            Dock = DockStyle.Top,
            Padding = new Padding(0, 0, 0, 8)
        };

        var instructionLabel = new Label
        {
            Text = isCreateNew ? CreateNewInstructions() : ExistingAccountInstructions(),
            AutoSize = false,
            Font = new Font("Segoe UI", 10f),
            ForeColor = Color.FromArgb(0x33, 0x33, 0x33),
            Dock = DockStyle.Top,
            Padding = new Padding(0, 0, 0, 8)
        };

        var noteLabel = new Label
        {
            Text = isCreateNew
                ? "Note: Installing for all users places launchers in Program Files, making them accessible to the gaming account."
                : "Note: The gaming account will still have its own separate launcher sessions even when launchers are installed for all users.",
            AutoSize = false,
            Font = new Font("Segoe UI", 9f, FontStyle.Italic),
            ForeColor = SystemColors.GrayText,
            Dock = DockStyle.Top,
            Padding = new Padding(0, 4, 0, 0)
        };

        TrackWrappingLabel(titleLabel);
        TrackWrappingLabel(instructionLabel);
        TrackWrappingLabel(noteLabel);

        // Reverse order: last added = highest z-order = docks to top first (displays topmost)
        Controls.AddRange(noteLabel, instructionLabel, titleLabel);
        ResumeLayout(false);
    }

    private static string CreateNewInstructions() =>
        "Before the gaming account is created, complete these steps:\n\n" +
        "1. Install each game launcher (Steam, Epic Games, GOG Galaxy, EA Desktop, Ubisoft Connect, etc.) " +
        "choosing \"Install for all users\" so they are placed in Program Files.\n\n" +
        "2. Create game library folders (e.g. D:\\Games\\Steam, D:\\Games\\GOG) \u2014 " +
        "recommended via each launcher\u2019s settings, but creating them manually in Explorer also works. " +
        "You will select these folders in the next step so the gaming account gets full access.\n\n" +
        "3. Log out of all launcher accounts completely (including from the system tray).\n\n" +
        "4. Close all launchers fully \u2014 right-click their tray icons and choose Exit/Quit.\n\n" +
        "Once all launchers are installed and fully closed, click Next to continue.";

    private static string ExistingAccountInstructions() =>
        "Before proceeding, complete these steps:\n\n" +
        "1. If the launcher installer offers a \"Install for current user only\" option, " +
        "use it while logged in as the gaming account. " +
        "This keeps the launcher private to that account. " +
        "Most installers do not offer this \u2014 if unavailable, install for all users instead.\n\n" +
        "2. Create game library folders (e.g. D:\\Games\\Steam, D:\\Games\\GOG) \u2014 " +
        "recommended via each launcher\u2019s settings, but creating them manually in Explorer also works. " +
        "You will select these folders in the next step so the gaming account gets full access.\n\n" +
        "Once done, click Next to continue.";
}