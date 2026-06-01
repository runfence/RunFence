namespace RunFence.Core;

public interface IProgramFilesPathProvider
{
    IReadOnlyList<string> GetProgramFilesRoots();
}
