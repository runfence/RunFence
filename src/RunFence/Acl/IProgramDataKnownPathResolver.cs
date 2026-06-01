namespace RunFence.Acl;

public interface IProgramDataKnownPathResolver
{
    string GetDirectoryPath(ProgramDataDirectoryPolicy policy);
    string GetFilePath(ProgramDataFilePolicy policy);
}
