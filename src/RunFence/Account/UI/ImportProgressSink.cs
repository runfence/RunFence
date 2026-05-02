namespace RunFence.Account.UI;

/// <summary>
/// Concrete <see cref="IImportProgressSink"/> implementation that binds pre-formed callbacks
/// for log output, completion signaling, and status updates.
/// The file path is pre-selected by the caller and returned as-is from <see cref="SelectFile"/>.
/// </summary>
internal sealed class ImportProgressSink(
    string selectedFilePath,
    Action<string> appendLog,
    Action enableOk,
    Action<string> updateStatus) : IImportProgressSink
{
    public string SelectFile() => selectedFilePath;

    public void AppendLog(string text) => appendLog(text);

    public void EnableOk() => enableOk();

    public void UpdateStatus(string text) => updateStatus(text);
}
