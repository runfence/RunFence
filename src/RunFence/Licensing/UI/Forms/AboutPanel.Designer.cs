#nullable disable

using System.ComponentModel;

namespace RunFence.Licensing.UI.Forms;

partial class AboutPanel
{
    private IContainer components = null;

    private Panel _headerPanel;
    private Label _appNameLabel;
    private Label _versionLabel;
    private Label _taglineLabel;
    private Panel _separatorPanel1;
    private Label _authorLabel;
    private LinkLabel _emailLink;
    private LinkLabel _gitHubLink;
    private LinkLabel _readmeLink;
    private Label _licenseStatusLabel;
    private Button _activateLicenseButton;
    private Label _pubKeyLabel;
    private TextBox _pubKeyHashTextBox;
    private TableLayoutPanel _contentPanel;
    private Panel _contentGroupPanel;
    private Panel _contentBorderPanel;

    private AboutPanel() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _headerPanel = new Panel();
        _appNameLabel = new Label();
        _versionLabel = new Label();
        _taglineLabel = new Label();
        _separatorPanel1 = new Panel();
        _authorLabel = new Label();
        _emailLink = new LinkLabel();
        _gitHubLink = new LinkLabel();
        _readmeLink = new LinkLabel();
        _licenseStatusLabel = new Label();
        _activateLicenseButton = new Button();
        _pubKeyLabel = new Label();
        _pubKeyHashTextBox = new TextBox();
        _contentPanel = new TableLayoutPanel();
        _contentGroupPanel = new Panel();
        _contentBorderPanel = new Panel();

        SuspendLayout();
        _headerPanel.SuspendLayout();
        _contentPanel.SuspendLayout();

        // _headerPanel — accent-colored header matching the nag dialog style
        _headerPanel.Dock = DockStyle.Top;
        _headerPanel.Height = 92;
        _headerPanel.BackColor = Color.FromArgb(0, 99, 177);
        _headerPanel.Controls.Add(_appNameLabel);
        _headerPanel.Controls.Add(_versionLabel);

        // _appNameLabel (inside _headerPanel)
        _appNameLabel.Text = "RunFence";
        _appNameLabel.Font = new Font("Segoe UI", 20f, FontStyle.Bold, GraphicsUnit.Point);
        _appNameLabel.ForeColor = Color.White;
        _appNameLabel.BackColor = Color.Transparent;
        _appNameLabel.AutoSize = true;
        _appNameLabel.Location = new Point(22, 10);

        // _versionLabel (inside _headerPanel)
        _versionLabel.Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
        _versionLabel.ForeColor = Color.FromArgb(210, 235, 255);
        _versionLabel.BackColor = Color.Transparent;
        _versionLabel.AutoSize = true;
        _versionLabel.Location = new Point(24, 52);

        // _contentPanel — fixed-width column ensures consistent left-alignment
        _contentPanel.ColumnCount = 1;
        _contentPanel.RowCount = 14;
        _contentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 560));
        _contentPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 0: tagline
        _contentPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 16)); // 1: spacer
        _contentPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 2: author
        _contentPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 3: email
        _contentPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 4: github
        _contentPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 5: readme
        _contentPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20)); // 6: spacer
        _contentPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 7: license status
        _contentPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 8: register button
        _contentPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 60)); // 9: spacer
        _contentPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));  // 10: separator
        _contentPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 10)); // 11: spacer
        _contentPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 12: pub key label
        _contentPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 13: pub key textbox
        _contentPanel.AutoSize = true;

        // _taglineLabel
        _taglineLabel.Text = "Run each app under its own account — isolated, secure, single click.";
        _taglineLabel.AutoSize = true;
        _taglineLabel.Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
        _taglineLabel.ForeColor = Color.FromArgb(55, 60, 70);
        _taglineLabel.MaximumSize = new Size(560, 0);
        _taglineLabel.Margin = new Padding(0, 0, 0, 0);
        _contentPanel.Controls.Add(_taglineLabel, 0, 0);

        // _authorLabel
        _authorLabel.Text = "Author: Vladyslav (RunFence)";
        _authorLabel.AutoSize = true;
        _authorLabel.Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
        _authorLabel.Margin = new Padding(0, 0, 0, 4);
        _contentPanel.Controls.Add(_authorLabel, 0, 2);

        // _emailLink
        _emailLink.Text = "runfencedev@gmail.com";
        _emailLink.AutoSize = true;
        _emailLink.Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
        _emailLink.Margin = new Padding(0, 0, 0, 4);
        _emailLink.LinkClicked += OnEmailLinkClicked;
        _contentPanel.Controls.Add(_emailLink, 0, 3);

        // _gitHubLink
        _gitHubLink.Text = "https://github.com/RunFence/";
        _gitHubLink.AutoSize = true;
        _gitHubLink.Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
        _gitHubLink.Margin = new Padding(0, 0, 0, 2);
        _gitHubLink.LinkClicked += OnGitHubLinkClicked;
        _contentPanel.Controls.Add(_gitHubLink, 0, 4);

        // _readmeLink
        _readmeLink.Text = "README.md (Documentation)";
        _readmeLink.AutoSize = true;
        _readmeLink.Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
        _readmeLink.Margin = new Padding(0, 0, 0, 0);
        _readmeLink.LinkClicked += OnReadmeLinkClicked;
        _contentPanel.Controls.Add(_readmeLink, 0, 5);

        // _licenseStatusLabel
        _licenseStatusLabel.AutoSize = true;
        _licenseStatusLabel.Font = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Point);
        _licenseStatusLabel.Margin = new Padding(0, 0, 0, 6);
        _contentPanel.Controls.Add(_licenseStatusLabel, 0, 7);

        // _activateLicenseButton
        _activateLicenseButton.Text = "Purchase / Register\u2026";
        _activateLicenseButton.Size = new Size(200, 48);
        _activateLicenseButton.Font = new Font("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Point);
        _activateLicenseButton.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        _activateLicenseButton.UseVisualStyleBackColor = true;
        _activateLicenseButton.Margin = new Padding(0, 0, 0, 0);
        _activateLicenseButton.Click += OnActivateLicenseClick;
        _contentPanel.Controls.Add(_activateLicenseButton, 0, 8);

        // _separatorPanel1 (row 10)
        _separatorPanel1.Dock = DockStyle.Fill;
        _separatorPanel1.BackColor = Color.FromArgb(210, 210, 210);
        _separatorPanel1.Margin = new Padding(0);
        _contentPanel.Controls.Add(_separatorPanel1, 0, 10);

        // _pubKeyLabel
        _pubKeyLabel.Text = "Licensing System Fingerprint (compare with README to detect tampering):";
        _pubKeyLabel.AutoSize = true;
        _pubKeyLabel.Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        _pubKeyLabel.ForeColor = Color.FromArgb(80, 85, 100);
        _pubKeyLabel.Margin = new Padding(0, 0, 0, 2);
        _contentPanel.Controls.Add(_pubKeyLabel, 0, 12);

        // _pubKeyHashTextBox
        _pubKeyHashTextBox.ReadOnly = true;
        _pubKeyHashTextBox.Font = new Font("Consolas", 9f);
        _pubKeyHashTextBox.Width = 560;
        _pubKeyHashTextBox.BackColor = SystemColors.Window;
        _pubKeyHashTextBox.ForeColor = Color.FromArgb(80, 85, 100);
        _pubKeyHashTextBox.TabStop = false;
        _pubKeyHashTextBox.Margin = new Padding(0);
        _contentPanel.Controls.Add(_pubKeyHashTextBox, 0, 13);

        // _contentGroupPanel
        _contentGroupPanel.BackColor = Color.FromArgb(248, 249, 252);
        _contentGroupPanel.Controls.Add(_contentPanel);

        // _contentBorderPanel — provides visible colored border around _contentGroupPanel
        _contentBorderPanel.BackColor = Color.FromArgb(130, 160, 200);
        _contentBorderPanel.Controls.Add(_contentGroupPanel);

        // Initial values for Designer — always overridden at runtime by CenterContent() in OnLayout
        _contentPanel.Location = new Point(16, 12);
        _contentGroupPanel.Location = new Point(1, 1);
        _contentGroupPanel.Size = new Size(592, 440);
        _contentBorderPanel.Size = new Size(594, 442);
        _contentBorderPanel.Location = new Point(16, 108);

        // AboutPanel
        Dock = DockStyle.Fill;
        Controls.Add(_contentBorderPanel);
        Controls.Add(_headerPanel);

        _headerPanel.ResumeLayout(false);
        _headerPanel.PerformLayout();
        _contentPanel.ResumeLayout(false);
        _contentPanel.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }
}
