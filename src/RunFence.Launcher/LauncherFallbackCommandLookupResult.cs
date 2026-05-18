namespace RunFence.Launcher;

public readonly struct LauncherFallbackCommandLookupResult
{
    public LauncherFallbackCommandLookupStatus Status { get; }
    public string? Command { get; }

    private LauncherFallbackCommandLookupResult(LauncherFallbackCommandLookupStatus status, string? command)
    {
        Status = status;
        Command = command;
    }

    public static LauncherFallbackCommandLookupResult NotFound()
        => new(LauncherFallbackCommandLookupStatus.NotFound, null);

    public static LauncherFallbackCommandLookupResult RejectedRunFenceCommand()
        => new(LauncherFallbackCommandLookupStatus.RejectedRunFenceCommand, null);

    public static LauncherFallbackCommandLookupResult Resolved(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("Resolved command must contain a command.", nameof(command));

        return new(LauncherFallbackCommandLookupStatus.Resolved, command);
    }
}
