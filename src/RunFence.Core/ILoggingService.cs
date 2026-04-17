namespace RunFence.Core;

public interface ILoggingService
{
    string LogFilePath { get; }
    bool Enabled { get; set; }
    void Debug(string message);
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);

    /// <summary>
    /// Logs a fatal message unconditionally, regardless of <see cref="Enabled"/>.
    /// Use for crashes and unrecoverable errors that must always be recorded.
    /// </summary>
    void Fatal(string message, Exception? ex = null);
}