using System.Drawing.Drawing2D;

namespace RunFence.Wizard.UI.Forms;

/// <summary>
/// Pure rendering utilities for the wizard dialog.
/// Applies the modern flat/white styling and paints the step indicator dots.
/// No form state — all inputs are passed as parameters.
/// </summary>
public static class WizardStylingHelper
{
    private static readonly Color AccentColor = Color.FromArgb(0x19, 0x67, 0xD2);
    private static readonly Color DotOutlineColor = Color.FromArgb(0xCC, 0xCC, 0xCC);

    /// <summary>
    /// Applies the modern white/flat visual style to all dialog elements.
    /// Call once from the dialog constructor after <c>InitializeComponent</c>.
    /// </summary>
    public static void ApplyModernStyling(
        Form form,
        Panel headerPanel,
        Panel contentPanel,
        Panel footerPanel,
        Panel progressPanel,
        Label titleLabel,
        Label statusLabel,
        Label errorLabel)
    {
        form.BackColor = Color.White;
        headerPanel.BackColor = Color.White;
        contentPanel.BackColor = Color.White;
        footerPanel.BackColor = Color.FromArgb(0xF5, 0xF5, 0xF5);
        progressPanel.BackColor = Color.White;

        titleLabel.Font = new Font("Segoe UI Semibold", 14f, FontStyle.Regular);
        titleLabel.ForeColor = Color.FromArgb(0x1A, 0x1A, 0x1A);

        statusLabel.Font = new Font("Segoe UI", 9f);
        statusLabel.ForeColor = Color.FromArgb(0x44, 0x44, 0x44);

        errorLabel.Font = new Font("Segoe UI", 9f);
        errorLabel.ForeColor = Color.FromArgb(0xC0, 0x20, 0x20);
    }

    /// <summary>
    /// Paints the step-indicator dots inside the given <paramref name="panel"/>.
    /// Filled dots for completed/current steps, outlined dots for future steps.
    /// No dots are drawn when <paramref name="stepCount"/> is 1 or fewer (picker-only state).
    /// </summary>
    public static void PaintStepIndicator(
        Graphics g,
        Panel panel,
        int stepCount,
        int currentStepIndex)
    {
        if (stepCount <= 1)
            return;

        g.SmoothingMode = SmoothingMode.AntiAlias;

        const int dotSize = 10;
        const int dotSpacing = 18;
        int totalWidth = stepCount * dotSize + (stepCount - 1) * (dotSpacing - dotSize);
        int startX = (panel.Width - totalWidth) / 2;
        int y = (panel.Height - dotSize) / 2;

        for (int i = 0; i < stepCount; i++)
        {
            int x = startX + i * dotSpacing;
            var rect = new Rectangle(x, y, dotSize, dotSize);

            if (i <= currentStepIndex)
            {
                using var brush = new SolidBrush(AccentColor);
                g.FillEllipse(brush, rect);
            }
            else
            {
                using var pen = new Pen(DotOutlineColor, 1.5f);
                g.DrawEllipse(pen, rect);
            }
        }
    }

    /// <summary>
    /// Paints the thin separator line at the top of the footer panel.
    /// Wire this to the footer panel's Paint event.
    /// </summary>
    public static void PaintFooterSeparator(Graphics g, int panelWidth)
    {
        using var pen = new Pen(Color.FromArgb(0xE0, 0xE0, 0xE0), 1f);
        g.DrawLine(pen, 0, 0, panelWidth, 0);
    }
}