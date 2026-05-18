using System.Runtime.InteropServices;
using RunFence.Core;

namespace RunFence.Infrastructure;

public sealed class SyntheticInputSender(
    IKeyboardStateReader keyboardStateReader,
    IWindowInputApi windowInputApi,
    ITrayWarningSink trayWarningSink,
    ILoggingService log) : ISyntheticInputSender
{
    private const uint SyntheticPasteMarker = 0x52464350u;
    private const int VK_V = 0x56;
    private const int VK_INSERT = 0x2D;
    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT = 0x10;
    private const string InputInjectionFailureWarning = "Input injection failed. Press Ctrl+V again manually.";
    private bool _inputInjectionWarningShown;

    // Deliberately process-scoped: after the first failure the user already knows to retry manually.
    public void SendPaste(ClipboardPasteKind pasteKind)
    {
        bool useShiftInsert = pasteKind == ClipboardPasteKind.ShiftInsert;
        IntPtr marker = new(unchecked((int)SyntheticPasteMarker));
        int modifierVk = useShiftInsert ? VK_SHIFT : VK_CONTROL;
        int mainVk = useShiftInsert ? VK_INSERT : VK_V;
        uint mainDownFlags = useShiftInsert ? WindowNative.KeyeventfExtendedKey : 0u;
        uint mainUpFlags = mainDownFlags | WindowNative.KeyeventfKeyup;
        bool modifierHeld = keyboardStateReader.IsKeyDown(modifierVk);

        WindowNative.INPUT[] inputs = modifierHeld
            ? [
                new() { type = WindowNative.InputKeyboard, ki = new WindowNative.KEYBDINPUT { wVk = (ushort)mainVk, dwFlags = mainDownFlags, dwExtraInfo = marker } },
                new() { type = WindowNative.InputKeyboard, ki = new WindowNative.KEYBDINPUT { wVk = (ushort)mainVk, dwFlags = mainUpFlags, dwExtraInfo = marker } },
              ]
            : [
                new() { type = WindowNative.InputKeyboard, ki = new WindowNative.KEYBDINPUT { wVk = (ushort)modifierVk, dwExtraInfo = marker } },
                new() { type = WindowNative.InputKeyboard, ki = new WindowNative.KEYBDINPUT { wVk = (ushort)mainVk, dwFlags = mainDownFlags, dwExtraInfo = marker } },
                new() { type = WindowNative.InputKeyboard, ki = new WindowNative.KEYBDINPUT { wVk = (ushort)mainVk, dwFlags = mainUpFlags, dwExtraInfo = marker } },
                new() { type = WindowNative.InputKeyboard, ki = new WindowNative.KEYBDINPUT { wVk = (ushort)modifierVk, dwFlags = WindowNative.KeyeventfKeyup, dwExtraInfo = marker } },
              ];

        var sendResult = windowInputApi.SendInput(inputs);
        if (sendResult.SentCount == (uint)inputs.Length)
            return;

        log.Warn($"ClipboardPasteInterceptService: SendInput sent {sendResult.SentCount}/{inputs.Length} events (error {sendResult.LastError}).");
        if (!_inputInjectionWarningShown)
        {
            _inputInjectionWarningShown = true;
            trayWarningSink.ShowWarning(InputInjectionFailureWarning);
        }
    }
}
