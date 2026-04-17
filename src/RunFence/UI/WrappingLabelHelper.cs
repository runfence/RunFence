namespace RunFence.UI;

/// <summary>
/// Helper for computing the correct height for multi-line wrapping labels.
/// Uses <see cref="TextRenderer.MeasureText"/> (pure GDI measurement — no I/O) to calculate
/// the height required to display the label's text within the owner control's width.
/// </summary>
public static class WrappingLabelHelper
{
    /// <summary>
    /// Recomputes and applies the correct height for a wrapping label inside <paramref name="owner"/>.
    /// The label must have <c>AutoSize = false</c> so its height is explicitly controlled.
    /// </summary>
    public static void UpdateHeight(Control owner, Label label)
    {
        var w = owner.ClientSize.Width - owner.Padding.Horizontal - label.Padding.Horizontal;
        if (w <= 0)
            return;
        var sz = TextRenderer.MeasureText(label.Text, label.Font, new Size(w, int.MaxValue),
            TextFormatFlags.WordBreak);
        label.Height = sz.Height + label.Padding.Vertical;
    }
}
