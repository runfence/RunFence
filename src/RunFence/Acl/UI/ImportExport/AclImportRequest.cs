namespace RunFence.Acl.UI.ImportExport;

public sealed record AclImportRequest(
    AclExportData ExportData,
    AclManagerPendingChanges Pending,
    string Sid,
    bool IsContainer);
