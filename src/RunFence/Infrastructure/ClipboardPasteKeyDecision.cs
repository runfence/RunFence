namespace RunFence.Infrastructure;

public sealed class ClipboardPasteKeyDecision(IKeyboardStateReader keyboardStateReader)
{
    private const int VK_V = 0x56;
    private const int VK_INSERT = 0x2D;
    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT = 0x10;

    public ClipboardPasteKind Classify(uint message, uint virtualKey)
    {
        if (message != WindowNative.WM_KEYDOWN)
            return ClipboardPasteKind.None;

        if (virtualKey == VK_V && keyboardStateReader.IsKeyDown(VK_CONTROL))
            return ClipboardPasteKind.CtrlV;

        if (virtualKey == VK_INSERT && keyboardStateReader.IsKeyDown(VK_SHIFT))
            return ClipboardPasteKind.ShiftInsert;

        return ClipboardPasteKind.None;
    }
}
