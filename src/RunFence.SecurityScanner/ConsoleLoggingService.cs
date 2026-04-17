using RunFence.Core;

namespace RunFence.SecurityScanner;

/// <summary>
/// Minimal <see cref="ILoggingService"/> implementation for SecurityScanner (standalone tool).
/// Writes warnings to stderr so slow-call monitoring from NTTranslateApi surfaces in the console.
/// </summary>
internal class ConsoleLoggingService : ILoggingService
{
    public string LogFilePath => string.Empty;
    public bool Enabled { get; set; } = true;

    public void Debug(string message) { }
    public void Info(string message) { }
    public void Warn(string message) => Console.Error.WriteLine($"[WARN] {message}");
    public void Error(string message, Exception? ex = null) =>
        Console.Error.WriteLine(ex != null ? $"[ERROR] {message} | {ex.GetType().Name}: {ex.Message}" : $"[ERROR] {message}");
    public void Fatal(string message, Exception? ex = null) =>
        Console.Error.WriteLine(ex != null ? $"[FATAL] {message} | {ex}" : $"[FATAL] {message}");
}
