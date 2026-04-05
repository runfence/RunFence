using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.DragBridge.UI.Forms;

/// <summary>
/// Drag Bridge settings UI section: enable/disable checkbox + single hotkey picker.
/// Embedded in OptionsPanel.
/// </summary>
public partial class DragBridgeSection : UserControl
{
    private readonly KeysConverter _keysConverter = new();
    private bool _loading;

    /// <summary>Fired when any setting changes.</summary>
    public event Action? Changed;

    private void OnEnableCheckedChanged(object? sender, EventArgs e)
    {
        _hotkeyBox.Enabled = _enableCheckBox.Checked;
        if (!_loading)
            Changed?.Invoke();
    }

    private void OnHotkeyKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box)
            return;
        // Ignore bare modifier keys
        if (e.KeyCode is Keys.None or Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            return;

        e.SuppressKeyPress = true;

        bool winHeld = (NativeInterop.GetAsyncKeyState(0x5B) & 0x8000) != 0
                       || (NativeInterop.GetAsyncKeyState(0x5C) & 0x8000) != 0;
        int value = (int)e.KeyData;
        if (winHeld)
            value |= DragBridgeHotkeyHelper.WinModifierBit;

        // Require at least one modifier — bare keys are not valid hotkeys
        if (!DragBridgeHotkeyHelper.HasModifier(value))
            return;

        box.Tag = value;
        box.Text = FormatHotkeyText(value);
        Changed?.Invoke();
    }

    public void LoadFromSettings(AppSettings settings)
    {
        _loading = true;
        try
        {
            _enableCheckBox.Checked = settings.EnableDragBridge;
            _hotkeyBox.Enabled = settings.EnableDragBridge;

            _hotkeyBox.Tag = settings.DragBridgeCopyHotkey;
            _hotkeyBox.Text = FormatHotkeyText(settings.DragBridgeCopyHotkey);
        }
        finally
        {
            _loading = false;
        }
    }

    public void SaveToSettings(AppSettings settings)
    {
        settings.EnableDragBridge = _enableCheckBox.Checked;
        if (_hotkeyBox.Tag is int key)
            settings.DragBridgeCopyHotkey = key;
    }

    private string FormatHotkeyText(int keysValue)
    {
        bool hasWin = (keysValue & DragBridgeHotkeyHelper.WinModifierBit) != 0;
        var strippedKeys = (Keys)(keysValue & ~DragBridgeHotkeyHelper.WinModifierBit);
        var text = _keysConverter.ConvertToString(strippedKeys) ?? strippedKeys.ToString();
        return hasWin ? "Win+" + text : text;
    }
}