namespace RunFence.Apps.Shortcuts;

public interface IExecutableIconCountReader
{
    bool TryGetIconCount(string path, out int iconCount);
}

