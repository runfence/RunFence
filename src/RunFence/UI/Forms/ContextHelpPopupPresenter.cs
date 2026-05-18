namespace RunFence.UI.Forms;

public sealed class ContextHelpPopupPresenter : IDisposable
{
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;
    private const int WmMButtonDown = 0x0207;
    private const int WmNcLButtonDown = 0x00A1;
    private const int WmNcRButtonDown = 0x00A4;
    private const int WmNcMButtonDown = 0x00A7;
    private const int PopupMaxLogicalWidth = 420;
    private const int PopupHorizontalPaddingLogicalWidth = 14;
    private const int PopupVerticalPaddingLogicalWidth = 10;

    private readonly ContextHelpForm _form;
    private readonly ToolTip _popup;
    private readonly Font _popupFont;
    private IWin32Window? _popupTarget;
    private string? _popupText;

    public ContextHelpPopupPresenter(ContextHelpForm form)
    {
        _form = form;
        _popup = CreatePopup();
        _popupFont = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;
        _popup.Popup += OnPopupMeasure;
        _popup.Draw += OnPopupDraw;
    }

    public bool HasVisiblePopup => _popupTarget != null;

    public void Show(Control target, string text)
    {
        var anchorPoint = target is ContextHelpButton
            ? new Point(Math.Max(0, target.Width - _form.ScaleHelpLogicalPixels(4)), target.Height)
            : new Point(Math.Min(_form.ScaleHelpLogicalPixels(12), Math.Max(0, target.Width - 1)), target.Height);

        Show(target, anchorPoint, text);
    }

    public void Show(Control target, Point anchorPoint, string text)
    {
        Hide();
        _popupText = text;
        var screenAnchorPoint = target.PointToScreen(anchorPoint);
        var formAnchorPoint = _form.PointToClient(screenAnchorPoint);
        _popup.Show(text, _form, formAnchorPoint);
        _popupTarget = _form;
    }

    public void Hide()
    {
        if (_popupTarget is Control { IsDisposed: false } control)
            _popup.Hide(control);
        else if (_popupTarget != null)
            _popup.Hide(_popupTarget);

        _popupTarget = null;
        _popupText = null;
    }

    public bool IsPopupDismissMessage(Message m)
    {
        if (_popupTarget == null)
            return false;

        return m.Msg is WmLButtonDown or WmRButtonDown or WmMButtonDown or WmNcLButtonDown or WmNcRButtonDown or WmNcMButtonDown;
    }

    public void Dispose()
    {
        _popup.Popup -= OnPopupMeasure;
        _popup.Draw -= OnPopupDraw;
        _popup.Dispose();
    }

    private static ToolTip CreatePopup()
    {
        return new ToolTip
        {
            AutomaticDelay = 0,
            InitialDelay = 0,
            ReshowDelay = 0,
            AutoPopDelay = int.MaxValue,
            ShowAlways = true,
            UseAnimation = false,
            UseFading = false,
            IsBalloon = false,
            OwnerDraw = true
        };
    }

    private void OnPopupMeasure(object? sender, PopupEventArgs e)
    {
        var text = _popupText;
        if (string.IsNullOrEmpty(text))
            return;

        var padding = GetPopupPadding();
        var maxTextWidth = GetPopupMaxTextWidth();
        var measuredSize = TextRenderer.MeasureText(
            text,
            _popupFont,
            new Size(maxTextWidth, int.MaxValue),
            GetPopupTextFormatFlags());

        e.ToolTipSize = new Size(
            measuredSize.Width + padding.Horizontal,
            measuredSize.Height + padding.Vertical);
    }

    private void OnPopupDraw(object? sender, DrawToolTipEventArgs e)
    {
        e.DrawBackground();
        e.DrawBorder();

        var padding = GetPopupPadding();
        var textBounds = new Rectangle(
            e.Bounds.Left + padding.Left,
            e.Bounds.Top + padding.Top,
            Math.Max(0, e.Bounds.Width - padding.Horizontal),
            Math.Max(0, e.Bounds.Height - padding.Vertical));
        var text = string.IsNullOrEmpty(_popupText) ? e.ToolTipText : _popupText;
        TextRenderer.DrawText(
            e.Graphics,
            text,
            _popupFont,
            textBounds,
            SystemColors.InfoText,
            GetPopupTextFormatFlags());
    }

    private Padding GetPopupPadding()
    {
        var horizontalPadding = _form.ScaleHelpLogicalPixels(PopupHorizontalPaddingLogicalWidth);
        var verticalPadding = _form.ScaleHelpLogicalPixels(PopupVerticalPaddingLogicalWidth);
        return new Padding(
            horizontalPadding,
            verticalPadding,
            horizontalPadding,
            verticalPadding);
    }

    private int GetPopupMaxTextWidth()
    {
        var minWidth = _form.ScaleHelpLogicalPixels(240);
        var preferredWidth = _form.ScaleHelpLogicalPixels(PopupMaxLogicalWidth);
        var maxScreenWidth = Math.Max(
            minWidth,
            Screen.FromControl(_form).WorkingArea.Width - _form.ScaleHelpLogicalPixels(48));

        return Math.Min(preferredWidth, maxScreenWidth);
    }

    private static TextFormatFlags GetPopupTextFormatFlags()
    {
        return TextFormatFlags.WordBreak |
            TextFormatFlags.Left |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.NoPadding;
    }
}
