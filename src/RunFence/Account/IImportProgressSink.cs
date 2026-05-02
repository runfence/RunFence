namespace RunFence.Account;

/// <summary>
/// Abstracts the UI interactions needed during an account settings import:
/// file selection, progress logging, completion signaling, and status reporting.
/// </summary>
public interface IImportProgressSink
{
    /// <summary>Returns the path to the settings file to import, or null if cancelled.</summary>
    string? SelectFile();

    /// <summary>Appends a line to the progress log.</summary>
    void AppendLog(string text);

    /// <summary>Signals that the import is complete and the OK button should be enabled.</summary>
    void EnableOk();

    /// <summary>Updates the status label with the given text.</summary>
    void UpdateStatus(string text);
}
