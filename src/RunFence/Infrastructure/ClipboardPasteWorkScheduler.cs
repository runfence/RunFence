namespace RunFence.Infrastructure;

public sealed class ClipboardPasteWorkScheduler : IClipboardPasteWorkScheduler
{
    public void Run(Action action) => Task.Run(action);
}
