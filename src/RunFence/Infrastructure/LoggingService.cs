using RunFence.Core;

namespace RunFence.Infrastructure;

public class LoggingService(string? logFilePath = null) : ILoggingService
{
    private readonly object _lock = new();

    private volatile bool _enabled = true;

    public string LogFilePath { get; } = logFilePath ?? Constants.LogFilePath;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

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
        lock (_lock)
        {
            try
            {
                var dir = Path.GetDirectoryName(LogFilePath);
                if (dir != null && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                RotateIfNeeded();

                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(LogFilePath, line);
            }
            catch
            {
                // Logging should never crash the app
            }
        }
    }

    private void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(LogFilePath))
                return;
            var info = new FileInfo(LogFilePath);
            if (info.Length <= Constants.MaxLogFileSize)
                return;

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
}