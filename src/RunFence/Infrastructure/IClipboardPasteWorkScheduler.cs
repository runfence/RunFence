namespace RunFence.Infrastructure;

public interface IClipboardPasteWorkScheduler
{
    void Run(Action action);
}
