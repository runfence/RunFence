namespace RunFence.Startup;

public record StartupOptions(bool IsBackground, bool PinBypassed, bool GrantStartupRunAsUnlock = false);
