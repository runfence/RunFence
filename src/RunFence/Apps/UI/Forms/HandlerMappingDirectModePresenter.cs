namespace RunFence.Apps.UI.Forms;

/// <summary>
/// Manages direct-handler mode state in <see cref="HandlerMappingAddDialog"/>: auto-fills the handler
/// from the interactive user's registry when a key is entered, rebuilds the key combo for direct mode,
/// and commits the accepted add/edit result.
/// </summary>
internal sealed class HandlerMappingDirectModePresenter
{
    private readonly IInteractiveUserAssociationReader _interactiveReader;
    private readonly ComboBox _keyCombo;
    private readonly TextBox _handlerTextBox;

    public HandlerMappingDirectModePresenter(
        IInteractiveUserAssociationReader interactiveReader,
        ComboBox keyCombo,
        TextBox handlerTextBox)
    {
        _interactiveReader = interactiveReader;
        _keyCombo = keyCombo;
        _handlerTextBox = handlerTextBox;
    }

    /// <summary>
    /// Returns the trimmed handler value, or null if blank.
    /// </summary>
    public string? HandlerValue
    {
        get
        {
            var v = _handlerTextBox.Text.Trim();
            return string.IsNullOrEmpty(v) ? null : v;
        }
    }

    /// <summary>
    /// Rebuilds the key combo for direct mode (common options only) and restores the current text.
    /// Call when switching into direct mode.
    /// </summary>
    public void RebuildKeyComboSuggestions()
    {
        var current = _keyCombo.Text;
        _keyCombo.Items.Clear();
        _keyCombo.Items.AddRange(
            HandlerAssociationDialogValueHelper.CommonOptions.Cast<object>().ToArray());
        _keyCombo.Text = current;
    }

    /// <summary>
    /// Tries to auto-fill the handler text box from the interactive user's HKCU registry for the current key.
    /// Does nothing when the key is blank or not a valid association key.
    /// </summary>
    public void TryAutoFillHandler()
    {
        var key = HandlerAssociationDialogValueHelper.NormalizeKey(_keyCombo.Text);
        if (!string.IsNullOrEmpty(key) && AppHandlerRegistrationService.IsValidKey(key))
        {
            var handler = _interactiveReader.GetAssociationHandler(key);
            if (handler.HasValue)
                _handlerTextBox.Text = handler.Value.ClassName ?? handler.Value.Command ?? string.Empty;
        }
    }
}
