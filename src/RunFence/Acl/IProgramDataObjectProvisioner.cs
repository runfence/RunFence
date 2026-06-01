namespace RunFence.Acl;

public interface IProgramDataObjectProvisioner
{
    string EnsureDirectory(ProgramDataDirectoryPolicy policy);
    FileStream CreateOrReplaceFile(ProgramDataFilePolicy policy, FileShare share);
    void CreateOrRepairDirectory(ProgramDataExplicitDirectoryRequest request);
    FileStream CreateOrReplaceFile(ProgramDataExplicitFileRequest request);
    void CreateFile(ProgramDataExplicitFileRequest request, Action<Stream> writeContent);
}
