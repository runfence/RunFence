using System.Diagnostics;
using RunFence.Apps.UI;
using RunFence.Core;
using Timer = System.Windows.Forms.Timer;

namespace RunFence.Licensing.UI.Forms;

public partial class EvaluationNagDialog : Form
{
    private readonly ILicenseService _licenseService;
    private Timer? _countdownTimer;
    private int _secondsRemaining = 5;

    internal EvaluationNagDialog(ILicenseService licenseService, bool skipCountdown = false)
    {
        _licenseService = licenseService;
        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
        _machineCodeTextBox.Text = licenseService.MachineCode;
        _featuresLabel.Text =
            $"  \u2713  Unlimited app entries  (evaluation: up to {Constants.EvaluationMaxApps})\r\n" +
            $"  \u2713  Unlimited stored credentials  (evaluation: up to {Constants.EvaluationMaxCredentials})\r\n" +
            $"  \u2713  AppContainer sandboxing  (evaluation: up to {Constants.EvaluationMaxContainers})\r\n" +
            $"  \u2713  Hide accounts from logon screen  (evaluation: up to {Constants.EvaluationMaxHiddenAccounts})\r\n" +
            "  \u2713  Auto-lock and idle timeout\r\n" +
            $"  \u2713  Unlimited firewall whitelist entries  (evaluation: up to {Constants.EvaluationMaxFirewallAllowlistEntries})\r\n" +
            "  \u2713  Handler associations  (evaluation: browser only)";
        if (skipCountdown)
        {
            _secondsRemaining = 0;
            _continueButton.Enabled = true;
            _continueButton.Text = "Continue Evaluation";
        }
        else
        {
            _continueButton.Enabled = false;
            UpdateCountdownText();
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_secondsRemaining <= 0)
            return;
        _countdownTimer = new Timer { Interval = 1000 };
        _countdownTimer.Tick += OnCountdownTick;
        _countdownTimer.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_secondsRemaining > 0 && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _countdownTimer?.Stop();
        _countdownTimer?.Dispose();
        _countdownTimer = null;
        base.OnFormClosed(e);
    }

    private void OnCountdownTick(object? sender, EventArgs e)
    {
        _secondsRemaining--;
        if (_secondsRemaining <= 0)
        {
            _countdownTimer!.Stop();
            _continueButton.Enabled = true;
            _continueButton.Text = "Continue Evaluation";
        }
        else
        {
            UpdateCountdownText();
        }
    }

    private void UpdateCountdownText()
    {
        _continueButton.Text = $"Continue Evaluation ({_secondsRemaining}s)";
    }

    private void OnContinueClick(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void OnEnterKeyClick(object? sender, EventArgs e)
    {
        using var dlg = new LicenseActivationDialog(_licenseService);
        dlg.ShowDialog(this);
        if (_licenseService.IsLicensed)
        {
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private void OnCopyMachineCodeClick(object? sender, EventArgs e)
    {
        Clipboard.SetText(_machineCodeTextBox.Text);
    }

    private void OnPaymentLinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://github.com/runfence/RunFence/blob/master/PAYMENT.md")
            { UseShellExecute = true });
    }

    private void OnEmailLinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("mailto:runfencedev@gmail.com") { UseShellExecute = true });
    }
}