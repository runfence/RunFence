using System.Diagnostics;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Infrastructure;

public class LoggingService(string? logFilePath = null, long? maxFileSizeBytes = null) : ILoggingService, IDisposable
{
    private readonly Lock _lock = new();
    private readonly long _maxFileSizeBytes = maxFileSizeBytes ?? PathConstants.MaxLogFileSize;

    private volatile LogVerbosity _verbosity = LogVerbosity.Info;
    private StreamWriter? _writer;
    private bool _disposed;

    public string LogFilePath { get; } = logFilePath ?? PathConstants.LogFilePath;

    public bool Enabled
    {
        get => Verbosity != LogVerbosity.Off;
        set => Verbosity = value
            ? Verbosity == LogVerbosity.Off ? LogVerbosity.Info : Verbosity
            : LogVerbosity.Off;
    }

    public LogVerbosity Verbosity
    {
        get => _verbosity;
        set => _verbosity = value;
    }

    public void Debug(string message) => Log(LogVerbosity.Debug, "DEBUG", message);
    public void Info(string message) => Log(LogVerbosity.Info, "INFO", message);
    public void Warn(string message) => Log(LogVerbosity.Warning, "WARN", message);

    public void Error(string message, Exception? ex = null)
    {
        var logMessage = ex != null ? $"{message} | {ex}" : message;
        Log(LogVerbosity.Error, "ERROR", logMessage);
    }

    public void Fatal(string message, Exception? ex = null)
    {
        var logMessage = ex != null ? $"{message} | {ex}" : message;
        Log(LogVerbosity.Error, "FATAL", logMessage, force: true);
    }

    private void Log(LogVerbosity messageVerbosity, string level, string message, bool force = false)
    {
        if (!force && (Verbosity == LogVerbosity.Off || messageVerbosity > Verbosity))
            return;
        if (_disposed)
            return;
        lock (_lock)
        {
            if (_disposed)
                return;
            try
            {
                var dir = Path.GetDirectoryName(LogFilePath);
                if (dir != null && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                RotateIfNeeded();

                EnsureWriter();
                _writer!.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}");
                _writer.Flush();
            }
            catch
            {
                // Logging should never crash the app
            }
        }
    }

    private void EnsureWriter()
    {
        if (_writer != null)
            return;
        _writer = new StreamWriter(LogFilePath, append: true, System.Text.Encoding.UTF8) { AutoFlush = false };
    }

    private void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(LogFilePath))
                return;
            var info = new FileInfo(LogFilePath);
            if (info.Length <= _maxFileSizeBytes)
                return;

            // Close writer before rotating so the file can be moved
            _writer?.Dispose();
            _writer = null;

            var backup = LogFilePath + ".bak";
            if (File.Exists(backup))
            {
                try { File.Delete(backup); }
                catch { /* best effort — still attempt move */ }
            }
            File.Move(LogFilePath, backup);
        }
        catch (Exception ex)
        {
            try
            {
                EventLog.WriteEntry("Application",
                    $"RunFence: log rotation failed for '{LogFilePath}': {ex.Message}",
                    EventLogEntryType.Warning);
            }
            catch
            {
                // Event Log write also failed — nothing more we can do
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }
}
