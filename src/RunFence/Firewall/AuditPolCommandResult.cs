namespace RunFence.Firewall;

public sealed record AuditPolCommandResult(int ExitCode, string StandardOutput, string StandardError);
