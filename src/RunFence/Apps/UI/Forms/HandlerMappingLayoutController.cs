namespace RunFence.Apps.UI.Forms;

/// <summary>
/// Controls add/edit mode layout and app/direct mode switching for <see cref="HandlerMappingAddDialog"/>.
/// Manages row visibility in the <see cref="TableLayoutPanel"/> and the form <see cref="Form.ClientSize"/>.
/// </summary>
internal sealed class HandlerMappingLayoutController
{
    private readonly Form _form;
    private readonly TableLayoutPanel _layout;
    private readonly ToolStrip _modeToolStrip;
    private readonly Label _keyLabel;
    private readonly ComboBox _keyCombo;
    private readonly Label _appLabel;
    private readonly ComboBox _appCombo;
    private readonly Label _handlerLabel;
    private readonly TextBox _handlerTextBox;
    private readonly Label _templateLabel;
    private readonly TextBox _templateTextBox;
    private readonly CombinedPrefixesSection _combinedPrefixesSection;

    public HandlerMappingLayoutController(
        Form form,
        TableLayoutPanel layout,
        ToolStrip modeToolStrip,
        Label keyLabel,
        ComboBox keyCombo,
        Label appLabel,
        ComboBox appCombo,
        Label handlerLabel,
        TextBox handlerTextBox,
        Label templateLabel,
        TextBox templateTextBox,
        CombinedPrefixesSection combinedPrefixesSection)
    {
        _form = form;
        _layout = layout;
        _modeToolStrip = modeToolStrip;
        _keyLabel = keyLabel;
        _keyCombo = keyCombo;
        _appLabel = appLabel;
        _appCombo = appCombo;
        _handlerLabel = handlerLabel;
        _handlerTextBox = handlerTextBox;
        _templateLabel = templateLabel;
        _templateTextBox = templateTextBox;
        _combinedPrefixesSection = combinedPrefixesSection;
    }

    /// <summary>
    /// Switches the form between app mode and direct mode in add mode.
    /// Updates row heights, control visibility, and form height.
    /// </summary>
    public void SwitchAddMode(bool directMode)
    {
        _appLabel.Visible = !directMode;
        _appCombo.Visible = !directMode;
        _handlerLabel.Visible = directMode;
        _handlerTextBox.Visible = directMode;
        _templateLabel.Visible = !directMode;
        _templateTextBox.Visible = !directMode;
        _combinedPrefixesSection.Visible = !directMode;

        _layout.RowStyles[1] = new RowStyle(SizeType.Absolute, directMode ? 0F : 20F);
        _layout.RowStyles[2] = new RowStyle(SizeType.Absolute, directMode ? 0F : 28F);
        _layout.RowStyles[3] = new RowStyle(SizeType.Absolute, directMode ? 20F : 0F);
        _layout.RowStyles[4] = new RowStyle(SizeType.Absolute, directMode ? 28F : 0F);
        _layout.RowStyles[7] = new RowStyle(SizeType.Absolute, directMode ? 0F : 20F);
        _layout.RowStyles[8] = new RowStyle(SizeType.Absolute, directMode ? 0F : 28F);
        _layout.RowStyles[9] = directMode
            ? new RowStyle(SizeType.Absolute, 0F)
            : new RowStyle(SizeType.Percent, 100F);

        _form.ClientSize = _form.ClientSize with { Height = directMode ? 240 : HandlerMappingAddDialog.AppMappingHeight };
    }

    /// <summary>
    /// Applies the edit-mode layout by hiding the mode tool strip and key row, then adjusting
    /// visibility and height for either app-edit or direct-handler-edit.
    /// </summary>
    public void ApplyEditLayout(bool directMode)
    {
        _modeToolStrip.Visible = false;
        _keyLabel.Visible = false;
        _keyCombo.Visible = false;
        _layout.RowStyles[0] = new RowStyle(SizeType.Absolute, 0F);
        _layout.RowStyles[5] = new RowStyle(SizeType.Absolute, 0F);
        _layout.RowStyles[6] = new RowStyle(SizeType.Absolute, 0F);

        if (directMode)
        {
            _handlerLabel.Visible = true;
            _handlerTextBox.Visible = true;
            _appLabel.Visible = false;
            _appCombo.Visible = false;
            _templateLabel.Visible = false;
            _templateTextBox.Visible = false;
            _combinedPrefixesSection.Visible = false;
            _layout.RowStyles[1] = new RowStyle(SizeType.Absolute, 0F);
            _layout.RowStyles[2] = new RowStyle(SizeType.Absolute, 0F);
            _layout.RowStyles[3] = new RowStyle(SizeType.Absolute, 20F);
            _layout.RowStyles[4] = new RowStyle(SizeType.Absolute, 28F);
            _layout.RowStyles[7] = new RowStyle(SizeType.Absolute, 0F);
            _layout.RowStyles[8] = new RowStyle(SizeType.Absolute, 0F);
            _layout.RowStyles[9] = new RowStyle(SizeType.Absolute, 0F);
            _form.ClientSize = _form.ClientSize with { Height = HandlerMappingAddDialog.DirectEditHeight };
        }
        else
        {
            _form.ClientSize = _form.ClientSize with { Height = HandlerMappingAddDialog.AppEditHeight };
        }
    }
}
