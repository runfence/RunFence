using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Launch;
using Timer = System.Windows.Forms.Timer;

namespace RunFence.Licensing.UI.Forms;

public partial class EvaluationNagDialog : Form
{
    private readonly ILicenseService _licenseService;
    private readonly ILaunchFacade _launchFacade;
    private Timer? _countdownTimer;
    private Timer? _flashTimer;
    private int _secondsRemaining = 5;

    internal EvaluationNagDialog(ILicenseService licenseService, ILaunchFacade launchFacade, bool skipCountdown = false)
    {
        _licenseService = licenseService;
        _launchFacade = launchFacade;
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
            // Flash the countdown button so the user understands why close was blocked.
            FlashCountdownButton();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _countdownTimer?.Stop();
        _countdownTimer?.Dispose();
        _countdownTimer = null;
        _flashTimer?.Stop();
        _flashTimer?.Dispose();
        _flashTimer = null;
        base.OnFormClosed(e);
    }

    private void FlashCountdownButton()
    {
        if (_flashTimer != null)
            return; // already flashing; ignore repeated close attempts during animation

        var flashText = $"\u23F3 Please wait... ({_secondsRemaining}s)";
        int flipCount = 0;

        _continueButton.Text = flashText;
        _flashTimer = new Timer { Interval = 250 };
        _flashTimer.Tick += (_, _) =>
        {
            flipCount++;
            // Odd ticks restore countdown text; even ticks show flash text.
            // Always use current _secondsRemaining so text stays accurate if countdown ticked during flash.
            _continueButton.Text = flipCount % 2 == 0 ? flashText : $"Continue Evaluation ({_secondsRemaining}s)";
            if (flipCount >= 5) // ~1.25s total, then restore
            {
                _flashTimer!.Stop();
                _flashTimer.Dispose();
                _flashTimer = null;
                _continueButton.Text = $"Continue Evaluation ({_secondsRemaining}s)";
            }
        };
        _flashTimer.Start();
    }

    private void OnCountdownTick(object? sender, EventArgs e)
    {
        _secondsRemaining--;
        if (_secondsRemaining <= 0)
        {
            _countdownTimer!.Stop();
            _flashTimer?.Stop();
            _flashTimer?.Dispose();
            _flashTimer = null;
            _continueButton.Enabled = true;
            _continueButton.Text = "Continue Evaluation";
        }
        else
        {
            // Skip updating the button text while a flash animation is running;
            // the flash will restore the correct text when it completes.
            if (_flashTimer == null)
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
        OpenUrl("https://github.com/runfence/RunFence/blob/master/PAYMENT.md");
    }

    private void OnEmailLinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        OpenUrl("mailto:runfencedev@gmail.com");
    }

    private void OpenUrl(string url) =>
        _launchFacade.LaunchUrl(url, AccountLaunchIdentity.InteractiveUser);
}