namespace RunFence.Infrastructure;

public readonly record struct ClipboardPasteTarget(
    IntPtr HWnd,
    int ForegroundProcessId,
    int TargetProcessId,
    uint ClipboardOwnerProcessId);
