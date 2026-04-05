namespace RunFence.Wizard.UI.Forms.Steps;

/// <summary>
/// Wizard step for collecting a new account's username and optional password.
/// Used by Quick Elevation (name only, 1-char limit), Gaming (name + password), and other account-creating templates.
/// </summary>
public class AccountNameStep : WizardStepPage
{
    private readonly Action<string, string> _setNameAndPassword;
    private readonly bool _showPassword;
    private readonly bool _requirePassword;
    private readonly int _maxNameLength;
    private readonly Func<string, bool>? _accountExists;

    private Label _usernameLabel = null!;
    private TextBox _usernameTextBox = null!;
    private Label _passwordLabel = null!;
    private TextBox _passwordTextBox = null!;
    private Label _descriptionLabel = null!;

    private const int DescHeight = 64;
    private const int DescGap = 8;

    public AccountNameStep(
        Action<string, string> setNameAndPassword,
        bool showPassword = false,
        int maxNameLength = 20,
        string? description = null,
        bool requirePassword = false,
        Func<string, bool>? accountExists = null)
    {
        _setNameAndPassword = setNameAndPassword;
        _showPassword = showPassword;
        _requirePassword = requirePassword;
        _maxNameLength = maxNameLength;
        _accountExists = accountExists;
        BuildContent(description);
    }

    public override string StepTitle => "Account Name";

    public override string? Validate()
    {
        var name = _usernameTextBox.Text.Trim();
        if (name.Length == 0)
            return "Please enter an account name.";
        if (name.Length > _maxNameLength)
            return $"Account name must be at most {_maxNameLength} character{(_maxNameLength == 1 ? "" : "s")}.";
        if (name.IndexOfAny(['/', '\\', '[', ']', ':', ';', '|', '=', ',', '+', '*', '?', '<', '>']) >= 0)
            return "Account name contains invalid characters.";
        if (_accountExists?.Invoke(name) == true)
            return "An account with this name already exists.";
        if (_requirePassword && _showPassword && _passwordTextBox.Text.Length == 0)
            return "A password is required so you can log in with Win+L.";
        return null;
    }

    public override void Collect()
    {
        var password = _showPassword ? _passwordTextBox.Text : string.Empty;
        _setNameAndPassword(_usernameTextBox.Text.Trim(), password);
    }

    private void BuildContent(string? description)
    {
        SuspendLayout();
        Padding = new Padding(8);

        bool hasDesc = !string.IsNullOrEmpty(description);
        int offset = hasDesc ? DescHeight + DescGap : 0;

        _descriptionLabel = new Label
        {
            Text = description ?? string.Empty,
            AutoSize = false,
            Font = new Font("Segoe UI", 9.5f),
            Location = new Point(0, 0),
            Width = 540,
            Height = DescHeight,
            Visible = hasDesc
        };

        _usernameLabel = new Label
        {
            Text = "Account name:",
            AutoSize = true,
            Font = new Font("Segoe UI", 10),
            Location = new Point(0, offset)
        };

        _usernameTextBox = new TextBox
        {
            Font = new Font("Segoe UI", 10),
            Location = new Point(0, offset + 22),
            Width = 300,
            MaxLength = _maxNameLength
        };

        _passwordLabel = new Label
        {
            Text = "Password:",
            AutoSize = true,
            Font = new Font("Segoe UI", 10),
            Location = new Point(0, offset + 56),
            Visible = _showPassword
        };

        _passwordTextBox = new TextBox
        {
            Font = new Font("Segoe UI", 10),
            Location = new Point(0, offset + 78),
            Width = 300,
            UseSystemPasswordChar = true,
            Visible = _showPassword
        };

        Controls.AddRange(_descriptionLabel, _usernameLabel, _usernameTextBox, _passwordLabel, _passwordTextBox);
        ResumeLayout(false);
    }
}