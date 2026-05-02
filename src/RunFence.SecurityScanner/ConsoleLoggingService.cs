using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.SecurityScanner;

/// <summary>
/// Minimal <see cref="ILoggingService"/> implementation for SecurityScanner (standalone tool).
/// Writes warnings to stderr so slow-call monitoring from NTTranslateApi surfaces in the console.
/// </summary>
internal class ConsoleLoggingService : ILoggingService
{
    public string LogFilePath => string.Empty;
    public bool Enabled
    {
        get => Verbosity != LogVerbosity.Off;
        set => Verbosity = value
            ? Verbosity == LogVerbosity.Off ? LogVerbosity.Warning : Verbosity
            : LogVerbosity.Off;
    }

    public LogVerbosity Verbosity { get; set; } = LogVerbosity.Warning;

    public void Debug(string message) => Write(LogVerbosity.Debug, $"[DEBUG] {message}");
    public void Info(string message) => Write(LogVerbosity.Info, $"[INFO] {message}");
    public void Warn(string message) => Write(LogVerbosity.Warning, $"[WARN] {message}");

    public void Error(string message, Exception? ex = null) => Write(
        LogVerbosity.Error,
        ex != null ? $"[ERROR] {message} | {ex.GetType().Name}: {ex.Message}" : $"[ERROR] {message}");

    public void Fatal(string message, Exception? ex = null) =>
        Console.Error.WriteLine(ex != null ? $"[FATAL] {message} | {ex}" : $"[FATAL] {message}");

    private void Write(LogVerbosity messageVerbosity, string message)
    {
        if (Verbosity == LogVerbosity.Off || messageVerbosity > Verbosity)
            return;

        Console.Error.WriteLine(message);
    }
}
