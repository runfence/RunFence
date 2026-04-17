using RunFence.Core;

namespace RunFence.Infrastructure;

public class LoggingService(string? logFilePath = null, long? maxFileSizeBytes = null) : ILoggingService, IDisposable
{
    private readonly object _lock = new();
    private readonly long _maxFileSizeBytes = maxFileSizeBytes ?? Constants.MaxLogFileSize;

    private volatile bool _enabled = true;
    private StreamWriter? _writer;
    private bool _disposed;

    public string LogFilePath { get; } = logFilePath ?? Constants.LogFilePath;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public void Debug(string message) => Log("DEBUG", message);
    public void Info(string message) => Log("INFO", message);
    public void Warn(string message) => Log("WARN", message);

    public void Error(string message, Exception? ex = null)
    {
        var logMessage = ex != null ? $"{message} | {ex.GetType().Name}: {ex.Message}" : message;
        Log("ERROR", logMessage);
    }

    public void Fatal(string message, Exception? ex = null)
    {
        var logMessage = ex != null ? $"{message} | {ex}" : message;
        Log("FATAL", logMessage, force: true);
    }

    private void Log(string level, string message, bool force = false)
    {
        if (!force && !Enabled)
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
                File.Delete(backup);
            File.Move(LogFilePath, backup);
        }
        catch
        {
            // Best effort rotation
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