namespace RunFence.Persistence.UI;

public interface IConfigImportExportFilePicker
{
    string? SelectSavePath(string filter, string title);

    string? SelectOpenPath(string filter, string title);
}
