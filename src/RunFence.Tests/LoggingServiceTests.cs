using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class LoggingServiceTests : IDisposable
{
    private readonly TempDirectory _tempDir;
    private readonly string _logPath;
    private readonly LoggingService _service;

    public LoggingServiceTests()
    {
        _tempDir = new TempDirectory("ram_logtest");
        _logPath = Path.Combine(_tempDir.Path, "test.log");
        _service = new LoggingService(_logPath, maxFileSizeBytes: 10_000);
    }

    public void Dispose()
    {
        _service.Dispose();
        _tempDir.Dispose();
    }

    private static string ReadLogContent(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs, System.Text.Encoding.UTF8);
        return sr.ReadToEnd();
    }

    [Theory]
    [InlineData("INFO", "test message")]
    [InlineData("WARN", "warning message")]
    [InlineData("ERROR", "error message")]
    public void Log_WritesCorrectLevel(string level, string message)
    {
        switch (level)
        {
            case "INFO":
                _service.Info(message);
                break;
            case "WARN":
                _service.Warn(message);
                break;
            case "ERROR":
                _service.Error(message);
                break;
        }

        var content = ReadLogContent(_logPath);
        Assert.Contains($"[{level}]", content);
        Assert.Contains(message, content);
    }

    [Fact]
    public void Error_WithException_IncludesExceptionInfo()
    {
        _service.Error("error occurred", new InvalidOperationException("bad state"));
        var content = ReadLogContent(_logPath);
        Assert.Contains("[ERROR]", content);
        Assert.Contains("error occurred", content);
        Assert.Contains("InvalidOperationException", content);
        Assert.Contains("bad state", content);
    }

    [Fact]
    public void Log_IncludesTimestamp()
    {
        _service.Info("timestamp test");
        var content = ReadLogContent(_logPath);
        // Matches pattern like "2026-02-24 12:34:56.789"
        Assert.Matches(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}", content);
    }

    [Fact]
    public void Log_CreatesDirectoryIfNeeded()
    {
        var nestedPath = Path.Combine(_tempDir.Path, "sub", "dir", "test.log");
        using var nestedService = new LoggingService(nestedPath);
        nestedService.Info("nested test");
        Assert.True(File.Exists(nestedPath));
    }

    [Fact]
    public void Log_RotatesWhenFileTooLarge()
    {
        // Write enough data to exceed the 10KB threshold (set in constructor), then stop.
        // Each message is ~1033 bytes (timestamp+level+1000-char body). After 10 writes
        // (~10.3KB) the file exceeds 10KB; rotation occurs on write 11. Write 2 more messages
        // after rotation so the new log stays smaller than the backup.
        var bigMessage = new string('X', 1_000);
        for (int i = 0; i < 13; i++)
        {
            _service.Info(bigMessage);
        }

        var bakPath = _logPath + ".bak";
        Assert.True(File.Exists(bakPath), "Backup file should exist after rotation");
        Assert.True(File.Exists(_logPath), "Log file should still exist after rotation");
        Assert.True(new FileInfo(_logPath).Length < new FileInfo(bakPath).Length,
            "Current log should be smaller than backup after rotation");
    }

    [Fact]
    public void Log_WhenDisabled_DoesNotWriteToFile()
    {
        _service.Enabled = false;
        _service.Info("should not appear");
        Assert.False(File.Exists(_logPath));
    }

    [Fact]
    public void Log_WhenReEnabled_WritesToFile()
    {
        _service.Enabled = false;
        _service.Info("invisible");
        _service.Enabled = true;
        _service.Info("visible");
        var content = ReadLogContent(_logPath);
        Assert.Contains("visible", content);
        Assert.DoesNotContain("invisible", content);
    }
}