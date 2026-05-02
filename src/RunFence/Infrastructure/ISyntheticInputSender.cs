namespace RunFence.Infrastructure;

public interface ISyntheticInputSender
{
    void SendPaste(ClipboardPasteKind pasteKind);
}
