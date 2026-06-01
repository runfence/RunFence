using RunFence.Acl.UI.ImportExport;

namespace RunFence.Acl.UI;

public interface IAclImportProcessor
{
    AclImportResult ProcessImport(AclImportRequest request);
}
