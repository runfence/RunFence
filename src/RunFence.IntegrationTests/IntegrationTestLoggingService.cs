using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.IntegrationTests;

internal sealed class IntegrationTestLoggingService : ILoggingService
{
    public string LogFilePath => "";
    public bool Enabled { get; set; }
    public LogVerbosity Verbosity { get; set; }
    public void Debug(string message) { }
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? ex = null) { }
    public void Fatal(string message, Exception? ex = null) { }
}
