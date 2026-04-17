using RunFence.UI;

namespace RunFence.Wizard.UI.Forms;

/// <summary>
/// Step 0 of the wizard — card-style template selection.
/// Each template is shown as a clickable card with an emoji icon, display name,
/// and description. Cards support hover highlighting and selected state with an accent border.
/// Double-clicking a card is equivalent to selecting it and clicking Next.
/// Templates marked <see cref="IWizardTemplate.IsPrerequisite"/> are sorted first,
/// drawn with an amber border, and a notice is shown urging the user to run them first.
/// </summary>
public class TemplatePickerStep : WizardStepPage
{
    private static readonly Color HoverBackColor = Color.FromArgb(0xE8, 0xF0, 0xFE);
    private static readonly Color SelectedBorderColor = Color.FromArgb(0x19, 0x67, 0xD2);
    private static readonly Color NormalBorderColor = Color.FromArgb(0xDD, 0xDD, 0xDD);
    private static readonly Color PrerequisiteBorderColor = Color.FromArgb(0xD4, 0x7A, 0x00);
    private static readonly Color PrerequisiteBackColor = Color.FromArgb(0xFF, 0xFB, 0xF2);

    private readonly FlowLayoutPanel _flowPanel;
    private readonly List<TemplateCard> _cards = [];
    private TemplateCard? _selectedCard;

    /// <summary>The currently selected template, or <c>null</c> if none is selected.</summary>
    public IWizardTemplate? SelectedTemplate => _selectedCard?.Template;

    public TemplatePickerStep(IReadOnlyList<IWizardTemplate> templates)
    {
        var introLabel = new Label
        {
            Text = "Choose a setup template to get started:",
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 28,
            Font = new Font("Segoe UI", 10f),
            ForeColor = Color.FromArgb(0x44, 0x44, 0x44),
            Padding = new Padding(0, 4, 0, 4)
        };

        bool hasPrerequisite = templates.Any(t => t.IsPrerequisite);
        Label? noticeLabel = null;
        if (hasPrerequisite)
        {
            noticeLabel = new Label
            {
                Text = "\u26A0\uFE0F It is recommended to run \u2018Prepare System\u2019 first \u2014 " +
                       "other templates work best when data drives have already been secured with restricted ACLs.",
                AutoSize = false,
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(0x7A, 0x50, 0x00),
                BackColor = Color.FromArgb(0xFF, 0xF3, 0xCD),
                Dock = DockStyle.Top,
                Padding = new Padding(8, 5, 8, 5)
            };
            TrackWrappingLabel(noticeLabel);
        }

        _flowPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 0),
            BackColor = Color.White
        };
        _flowPanel.ClientSizeChanged += (_, _) => UpdateCardWidths();

        foreach (var template in templates)
        {
            var card = new TemplateCard(template);
            card.Margin = Padding.Empty;
            card.CardSelected += OnCardSelected;
            card.CardDoubleClicked += OnCardDoubleClicked;
            _cards.Add(card);
            _flowPanel.Controls.Add(card);
        }

        // Spacer so the last card's bottom border is fully visible when scrolled to the end.
        _flowPanel.Controls.Add(new Panel { Height = 8, Margin = Padding.Empty, BackColor = Color.White });

        // Add controls: Fill first, then Top items (last added = topmost in DockStyle.Top stack).
        Controls.Add(_flowPanel);
        if (noticeLabel != null)
            Controls.Add(noticeLabel);
        Controls.Add(introLabel);

        BackColor = Color.White;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateCardWidths();
    }

    private void UpdateCardWidths()
    {
        // Use _flowPanel.ClientSize.Width so cards shrink correctly when a vertical scrollbar appears.
        int cardWidth = _flowPanel.ClientSize.Width - 4;
        if (cardWidth <= 0)
            return;
        foreach (var card in _cards)
            card.Width = cardWidth;
    }

    public override bool CanProceed => _selectedCard != null;

    private void OnCardSelected(TemplateCard card)
    {
        if (_selectedCard == card)
            return;
        _selectedCard?.Deselect();
        _selectedCard = card;
        _selectedCard.Select();
        NotifyCanProceedChanged();
    }

    private void OnCardDoubleClicked(TemplateCard card)
    {
        OnCardSelected(card);
        DoubleClickedToAdvance?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the user double-clicks a card, requesting immediate advancement.</summary>
    public event EventHandler? DoubleClickedToAdvance;

    public override string StepTitle => "Choose a Template";

    public override string? Validate() =>
        _selectedCard == null ? "Please select a template to continue." : null;

    public override void Collect()
    {
        /* SelectedTemplate property carries the selection */
    }

    // -----------------------------------------------------------------------------------------
    // Inner type: individual template card
    // -----------------------------------------------------------------------------------------

    private sealed class TemplateCard : Panel
    {
        private const int CardHeight = 82;
        private const int IconSize = 40;
        private const int IconLeft = 12;
        private const int TextLeft = IconLeft + IconSize + 12;

        // Shared GDI resources — disposed when the application exits (static, never replaced)
        private static readonly Font NameFont = new("Segoe UI Semibold", 11f, FontStyle.Regular);
        private static readonly Font DescFont = new("Segoe UI", 9f);
        private static readonly Color NameColor = Color.FromArgb(0x1A, 0x1A, 0x1A);
        private static readonly Color DescColor = Color.FromArgb(0x66, 0x66, 0x66);

        private bool _isHovered;
        private bool _isSelected;
        private readonly Image? _icon;
        private readonly bool _isPrerequisite;

        public IWizardTemplate Template { get; }

        public event Action<TemplateCard>? CardSelected;
        public event Action<TemplateCard>? CardDoubleClicked;

        public TemplateCard(IWizardTemplate template)
        {
            Template = template;
            _isPrerequisite = template.IsPrerequisite;
            Height = CardHeight;
            Cursor = Cursors.Hand;
            BackColor = Color.White;

            _icon = UiIconFactory.CreateToolbarIcon(template.IconEmoji, Color.FromArgb(0x33, 0x66, 0x99), IconSize);

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var bounds = ClientRectangle;

            // Background
            Color bgColor;
            if (_isSelected || _isHovered)
                bgColor = HoverBackColor;
            else if (_isPrerequisite)
                bgColor = PrerequisiteBackColor;
            else
                bgColor = Color.White;

            using (var bgBrush = new SolidBrush(bgColor))
                g.FillRectangle(bgBrush, bounds);

            // Border
            Color borderColor;
            int borderWidth;
            if (_isSelected)
            {
                borderColor = SelectedBorderColor;
                borderWidth = 2;
            }
            else if (_isPrerequisite)
            {
                borderColor = PrerequisiteBorderColor;
                borderWidth = 2;
            }
            else
            {
                borderColor = NormalBorderColor;
                borderWidth = 1;
            }

            using (var borderPen = new Pen(borderColor, borderWidth))
            {
                var borderRect = bounds with { Width = bounds.Width - 1, Height = bounds.Height - 1 };
                g.DrawRectangle(borderPen, borderRect);
            }

            // Icon
            if (_icon != null)
            {
                int iconY = (bounds.Height - IconSize) / 2;
                g.DrawImage(_icon, IconLeft, iconY, IconSize, IconSize);
            }

            // Template name
            var nameRect = new Rectangle(TextLeft, 12, bounds.Width - TextLeft - 8, 22);
            TextRenderer.DrawText(g, Template.DisplayName, NameFont, nameRect, NameColor,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            // Description (up to 2 lines)
            var descRect = new Rectangle(TextLeft, 36, bounds.Width - TextLeft - 8, 38);
            TextRenderer.DrawText(g, Template.Description, DescFont, descRect, DescColor,
                TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _isHovered = false;
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            CardSelected?.Invoke(this);
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            CardDoubleClicked?.Invoke(this);
        }

        public new void Select()
        {
            _isSelected = true;
            Invalidate();
        }

        public void Deselect()
        {
            _isSelected = false;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _icon?.Dispose();
            base.Dispose(disposing);
        }
    }
}