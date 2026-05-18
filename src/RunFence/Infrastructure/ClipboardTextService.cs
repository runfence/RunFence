namespace RunFence.Infrastructure;

public sealed class ClipboardTextService : IClipboardTextService
{
    public void SetText(string text) => Clipboard.SetText(text);
}
