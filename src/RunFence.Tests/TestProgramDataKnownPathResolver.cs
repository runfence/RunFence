using RunFence.Acl;

namespace RunFence.Tests;

internal sealed class TestProgramDataKnownPathResolver(string rootPath) : IProgramDataKnownPathResolver
{
    public string GetDirectoryPath(ProgramDataDirectoryPolicy policy)
        => Path.Combine(rootPath, policy.RelativePath);

    public string GetFilePath(ProgramDataFilePolicy policy)
        => Path.Combine(rootPath, policy.RelativePath);
}
