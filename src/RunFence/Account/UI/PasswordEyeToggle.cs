using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;

namespace RunFence.Account.UI;

/// <summary>
/// Adds an eye toggle button inside a password TextBox for show/hide functionality.
/// Also installs a NativeWindow subclass to exclude clipboard content from history monitors.
/// </summary>
public static class PasswordEyeToggle
{
    private const int EM_SETMARGINS = 0xD3;
    private const int EC_RIGHTMARGIN = 2;
    private const int WM_COPY = 0x0301;
    private const int WM_CUT = 0x0300;
    private const uint WDA_NONE = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    // Per-form reference count of currently-revealed password controls.
    // Only the first reveal sets the affinity; only the last un-reveal restores it.
    // Weak keys ensure forms are not kept alive by this table.
    private static readonly ConditionalWeakTable<Form, RevealCounter> _formRevealCounts = new();

    private sealed class RevealCounter { public int Count; }

    /// <summary>
    /// Adds an eye toggle button inside the given password TextBox.
    /// The TextBox must have UseSystemPasswordChar = true set before calling.
    /// </summary>
    public static void AddTo(TextBox textBox)
    {
        // Install clipboard interception to exclude password copies from clipboard history
        var clipboardInterceptor = new ClipboardInterceptor(textBox);
        textBox.Disposed += (_, _) => clipboardInterceptor.ReleaseHandle();

        var btnWidth = textBox.Height;
        var iconSize = Math.Max(12, textBox.ClientSize.Height - 4);

        // Pre-create both images to avoid repeated allocations and leaks
        var eyeOpen = CreateEyeImage(iconSize, slashed: false);
        var eyeSlashed = CreateEyeImage(iconSize, slashed: true);

        var btn = new Button
        {
            Size = textBox.ClientSize with { Width = btnWidth },
            Location = new Point(textBox.ClientSize.Width - btnWidth, 0),
            Anchor = AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Default,
            TabStop = false,
            BackColor = SystemColors.Window,
            // Hidden → open eye (click to reveal); Visible → slashed eye (click to hide)
            Image = eyeOpen
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(0xE8, 0xE8, 0xE8);
        btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(0xD8, 0xD8, 0xD8);

        bool isRevealed = false;
        Form? cachedForm = null;

        void CacheForm()
        {
            cachedForm = textBox.FindForm();
        }

        if (textBox.IsHandleCreated)
            CacheForm();
        else
            textBox.HandleCreated += (_, _) => CacheForm();

        btn.Click += (_, _) =>
        {
            textBox.UseSystemPasswordChar = !textBox.UseSystemPasswordChar;
            isRevealed = !textBox.UseSystemPasswordChar;
            btn.Image = textBox.UseSystemPasswordChar ? eyeOpen : eyeSlashed;
            ApplyRevealAffinity(textBox, isRevealed);
            textBox.Focus();
        };

        btn.Disposed += (_, _) =>
        {
            eyeOpen.Dispose();
            eyeSlashed.Dispose();
        };

        textBox.Disposed += (_, _) =>
        {
            if (isRevealed)
                ApplyRevealAffinity(cachedForm, false);
        };

        textBox.Controls.Add(btn);

        // Sync button appearance with textbox enabled state
        UpdateButtonForEnabledState(btn, textBox.Enabled);
        textBox.EnabledChanged += (_, _) => UpdateButtonForEnabledState(btn, textBox.Enabled);

        // Set right margin on the edit control to prevent text from overlapping the button.
        // EM_SETMARGINS HIWORD of lParam = right margin in pixels.
        if (textBox.IsHandleCreated)
            ApplyRightMargin(textBox, btnWidth);
        else
            textBox.HandleCreated += (_, _) => ApplyRightMargin(textBox, btnWidth);
    }

    /// <summary>
    /// Applies or removes WDA_EXCLUDEFROMCAPTURE on the form that owns <paramref name="control"/>.
    /// Reference-counted so multiple password boxes on the same form cooperate correctly.
    /// </summary>
    public static void ApplyRevealAffinity(Control control, bool reveal)
        => ApplyRevealAffinity(control.FindForm(), reveal);

    /// <summary>
    /// Applies or removes WDA_EXCLUDEFROMCAPTURE on <paramref name="form"/>.
    /// Accepts a cached form reference so that callers on the disposal path (where
    /// <c>control.FindForm()</c> returns null) can still decrement the counter correctly.
    /// </summary>
    public static void ApplyRevealAffinity(Form? form, bool reveal)
    {
        if (form is null || !form.IsHandleCreated)
            return;

        var counter = _formRevealCounts.GetOrCreateValue(form);
        if (reveal)
        {
            counter.Count++;
            if (counter.Count == 1)
                PasswordEyeToggleNative.SetWindowDisplayAffinity(form.Handle, WDA_EXCLUDEFROMCAPTURE);
        }
        else
        {
            counter.Count = Math.Max(0, counter.Count - 1);
            if (counter.Count == 0)
                PasswordEyeToggleNative.SetWindowDisplayAffinity(form.Handle, WDA_NONE);
        }
    }

    internal static void UpdateButtonForEnabledState(Button btn, bool enabled)
    {
        btn.Enabled = enabled;
        btn.BackColor = enabled ? SystemColors.Window : SystemColors.Control;
        btn.FlatAppearance.MouseOverBackColor = enabled
            ? Color.FromArgb(0xE8, 0xE8, 0xE8)
            : SystemColors.Control;
        btn.FlatAppearance.MouseDownBackColor = enabled
            ? Color.FromArgb(0xD8, 0xD8, 0xD8)
            : SystemColors.Control;
    }

    private static void ApplyRightMargin(TextBox textBox, int margin)
    {
        PasswordEyeToggleNative.SendMessage(textBox.Handle, EM_SETMARGINS, EC_RIGHTMARGIN, margin << 16);
    }

    private sealed class ClipboardInterceptor : NativeWindow
    {
        private readonly TextBox _textBox;

        public ClipboardInterceptor(TextBox textBox)
        {
            _textBox = textBox;
            if (textBox.IsHandleCreated)
                AssignHandle(textBox.Handle);
            else
                textBox.HandleCreated += (_, _) => AssignHandle(textBox.Handle);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg is WM_COPY or WM_CUT)
            {
                var text = _textBox.SelectedText;
                if (!string.IsNullOrEmpty(text))
                {
                    var dataObject = new DataObject(DataFormats.UnicodeText, text);
                    dataObject.SetData("ExcludeClipboardContentFromMonitorProcessing",
                        new MemoryStream(new byte[4]));
                    Clipboard.SetDataObject(dataObject, copy: true);
                    if (m.Msg == WM_CUT)
                    {
                        var selStart = _textBox.SelectionStart;
                        _textBox.Text = _textBox.Text.Remove(selStart, _textBox.SelectionLength);
                        _textBox.SelectionStart = selStart;
                    }
                }

                return;
            }

            base.WndProc(ref m);
        }
    }

    internal static Image CreateEyeImage(int size, bool slashed)
    {
        if (size < 8)
            size = 12;
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var color = Color.FromArgb(0x55, 0x55, 0x55);
        var penWidth = Math.Max(1f, size / 14f);
        using var pen = new Pen(color, penWidth);
        using var brush = new SolidBrush(color);

        float cx = size / 2f, cy = size / 2f;
        float hw = size * 0.42f; // half eye width
        float hh = size * 0.18f; // half eye height (almond shape)
        float cp = hh * 1.8f; // moderate control point offset

        // Eye outline: two bezier curves forming an almond
        using var eyePath = new GraphicsPath();
        eyePath.AddBezier(cx - hw, cy, cx - hw * 0.4f, cy - cp, cx + hw * 0.4f, cy - cp, cx + hw, cy);
        eyePath.AddBezier(cx + hw, cy, cx + hw * 0.4f, cy + cp, cx - hw * 0.4f, cy + cp, cx - hw, cy);
        eyePath.CloseFigure();

        // White fill so iris is clearly visible
        using var whiteBrush = new SolidBrush(SystemColors.Window);
        g.FillPath(whiteBrush, eyePath);
        g.DrawPath(pen, eyePath);

        // Iris
        float ir = hh * 0.8f;
        g.DrawEllipse(pen, cx - ir, cy - ir, ir * 2, ir * 2);

        // Pupil
        float pr = ir * 0.45f;
        g.FillEllipse(brush, cx - pr, cy - pr, pr * 2, pr * 2);

        // Diagonal slash
        if (slashed)
        {
            // White background stroke to cut through the eye
            using var bgPen = new Pen(SystemColors.Window, Math.Max(2.5f, size / 6f));
            bgPen.StartCap = LineCap.Round;
            bgPen.EndCap = LineCap.Round;
            g.DrawLine(bgPen, cx - hw * 0.6f, cy + hh * 1.8f, cx + hw * 0.6f, cy - hh * 1.8f);

            using var slashPen = new Pen(color, Math.Max(1.2f, size / 12f));
            slashPen.StartCap = LineCap.Round;
            slashPen.EndCap = LineCap.Round;
            g.DrawLine(slashPen, cx - hw * 0.6f, cy + hh * 1.8f, cx + hw * 0.6f, cy - hh * 1.8f);
        }

        return bmp;
    }
}