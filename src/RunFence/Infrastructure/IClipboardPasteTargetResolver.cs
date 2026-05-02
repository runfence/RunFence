namespace RunFence.Infrastructure;

public interface IClipboardPasteTargetResolver
{
    ClipboardPasteTargetResolution Resolve();
}
