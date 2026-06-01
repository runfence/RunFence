using Microsoft.Win32.SafeHandles;

namespace RunFence.Acl;

public interface IProgramDataPathGuard
{
    string NormalizeRoot();
    string NormalizeRelativePath(string relativePath);
    string NormalizeAbsolutePathUnderRoot(string path);
    string NormalizeExistingPathUnderRoot(string path, ProgramDataObjectKind kind);
    SafeFileHandle OpenExistingManagedObject(string path, ProgramDataObjectKind kind, ProgramDataManagedObjectAccess access);
    bool IsUnderRoot(string path);
}
