#nullable disable

using System.ComponentModel;

namespace RunFence.Licensing.UI.Forms;

partial class EvaluationNagDialog
{
    private IContainer components = null;

    private Panel _headerPanel;
    private Label _headingLabel;
    private Label _bodyLabel;
    private Label _sectionLabel;
    private Label _featuresLabel;
    private Panel _separatorPanel1;
    private Label _machineCodeLabel;
    private TextBox _machineCodeTextBox;
    private Button _copyMachineCodeButton;
    private LinkLabel _paymentLink;
    private LinkLabel _emailLink;
    private Label _orgLabel;
    private Panel _separatorPanel2;
    private Button _enterKeyButton;
    private Button _continueButton;

    private EvaluationNagDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _headerPanel = new Panel();
        _headingLabel = new Label();
        _bodyLabel = new Label();
        _sectionLabel = new Label();
        _featuresLabel = new Label();
        _separatorPanel1 = new Panel();
        _machineCodeLabel = new Label();
        _machineCodeTextBox = new TextBox();
        _copyMachineCodeButton = new Button();
        _paymentLink = new LinkLabel();
        _emailLink = new LinkLabel();
        _orgLabel = new Label();
        _separatorPanel2 = new Panel();
        _enterKeyButton = new Button();
        _continueButton = new Button();

        SuspendLayout();
        _headerPanel.SuspendLayout();

        // _headerPanel
        _headerPanel.BackColor = Color.FromArgb(0, 99, 177);
        _headerPanel.Location = new Point(0, 0);
        _headerPanel.Size = new Size(900, 100);
        _headerPanel.Controls.Add(_headingLabel);
        _headerPanel.Controls.Add(_bodyLabel);

        // _headingLabel (inside _headerPanel)
        _headingLabel.Text = "RunFence — Evaluation";
        _headingLabel.Font = new Font("Segoe UI", 20f, FontStyle.Bold);
        _headingLabel.ForeColor = Color.White;
        _headingLabel.BackColor = Color.Transparent;
        _headingLabel.AutoSize = true;
        _headingLabel.MaximumSize = new Size(856, 0);
        _headingLabel.Location = new Point(22, 14);

        // _bodyLabel (inside _headerPanel)
        _bodyLabel.Text = "Paid license required to remove limitations  ·  Evaluation use is free with no time limit";
        _bodyLabel.Font = new Font("Segoe UI", 10f);
        _bodyLabel.ForeColor = Color.FromArgb(210, 235, 255);
        _bodyLabel.BackColor = Color.Transparent;
        _bodyLabel.AutoSize = true;
        _bodyLabel.MaximumSize = new Size(856, 0);
        _bodyLabel.Location = new Point(24, 63);

        // _sectionLabel
        _sectionLabel.Text = "Purchase a license to unlock:";
        _sectionLabel.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
        _sectionLabel.ForeColor = Color.FromArgb(0, 99, 177);
        _sectionLabel.AutoSize = true;
        _sectionLabel.Location = new Point(24, 116);

        // _featuresLabel
        _featuresLabel.Font = new Font("Segoe UI", 11f);
        _featuresLabel.AutoSize = true;
        _featuresLabel.MaximumSize = new Size(852, 0);
        _featuresLabel.Location = new Point(28, 148);
        _featuresLabel.Text = "  \u2713  Unlimited app entries\r\n  \u2713  Unlimited stored credentials\r\n  \u2713  AppContainer sandboxing\r\n  \u2713  Hide accounts from logon screen\r\n  \u2713  Auto-lock and idle timeout\r\n  \u2713  Internet whitelist\r\n  \u2713  Handler associations";

        // _separatorPanel1
        _separatorPanel1.BackColor = Color.FromArgb(210, 210, 210);
        _separatorPanel1.Location = new Point(24, 342);
        _separatorPanel1.Size = new Size(852, 1);

        // _machineCodeLabel
        _machineCodeLabel.Text = "Machine Code — include in your purchase email to receive a license key:";
        _machineCodeLabel.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        _machineCodeLabel.AutoSize = true;
        _machineCodeLabel.Location = new Point(24, 358);

        // _machineCodeTextBox
        _machineCodeTextBox.ReadOnly = true;
        _machineCodeTextBox.Font = new Font("Consolas", 10f);
        _machineCodeTextBox.Location = new Point(24, 382);
        _machineCodeTextBox.Size = new Size(766, 24);
        _machineCodeTextBox.BackColor = SystemColors.Window;
        _machineCodeTextBox.TabStop = false;

        // _copyMachineCodeButton
        _copyMachineCodeButton.Text = "Copy";
        _copyMachineCodeButton.Location = new Point(798, 380);
        _copyMachineCodeButton.Size = new Size(78, 28);
        _copyMachineCodeButton.Click += OnCopyMachineCodeClick;

        // _paymentLink
        _paymentLink.Text = "Purchase";
        _paymentLink.Font = new Font("Segoe UI", 10f);
        _paymentLink.AutoSize = true;
        _paymentLink.Location = new Point(24, 426);
        _paymentLink.LinkClicked += OnPaymentLinkClicked;

        // _orgLabel
        _orgLabel.Text = "  ·  ";
        _orgLabel.Font = new Font("Segoe UI", 10f);
        _orgLabel.AutoSize = true;
        _orgLabel.Location = new Point(104, 426);

        // _emailLink
        _emailLink.Text = "runfencedev@gmail.com";
        _emailLink.Font = new Font("Segoe UI", 10f);
        _emailLink.AutoSize = true;
        _emailLink.MinimumSize = new Size(170, 0);
        _emailLink.Location = new Point(140, 426);
        _emailLink.LinkClicked += OnEmailLinkClicked;

        // _separatorPanel2
        _separatorPanel2.BackColor = Color.FromArgb(210, 210, 210);
        _separatorPanel2.Location = new Point(24, 460);
        _separatorPanel2.Size = new Size(852, 1);

        // _enterKeyButton
        _enterKeyButton.Text = "Enter License Key…";
        _enterKeyButton.Location = new Point(24, 480);
        _enterKeyButton.Size = new Size(240, 38);
        _enterKeyButton.Click += OnEnterKeyClick;

        // _continueButton
        _continueButton.Text = "Continue Evaluation (5s)";
        _continueButton.Location = new Point(280, 480);
        _continueButton.Size = new Size(596, 38);
        _continueButton.Click += OnContinueClick;

        // Form
        Text = "RunFence — Evaluation";
        ClientSize = new Size(900, 542);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(_headerPanel);
        Controls.Add(_sectionLabel);
        Controls.Add(_featuresLabel);
        Controls.Add(_separatorPanel1);
        Controls.Add(_machineCodeLabel);
        Controls.Add(_machineCodeTextBox);
        Controls.Add(_copyMachineCodeButton);
        Controls.Add(_paymentLink);
        Controls.Add(_orgLabel);
        Controls.Add(_emailLink);
        Controls.Add(_separatorPanel2);
        Controls.Add(_enterKeyButton);
        Controls.Add(_continueButton);

        _headerPanel.ResumeLayout(false);
        _headerPanel.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }
}
