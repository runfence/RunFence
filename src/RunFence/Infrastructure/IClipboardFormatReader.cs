namespace RunFence.Infrastructure;

public interface IClipboardFormatReader
{
    IntPtr GetClipboardOwnerWindow();
    IReadOnlyList<ClipboardFormatData> ReadGlobalMemoryFormats();
}
