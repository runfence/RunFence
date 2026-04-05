namespace RunFence.Startup;

public interface IStartupOptions
{
    bool IsBackground { get; }
}

public record StartupOptions(bool IsBackground) : IStartupOptions;