namespace RunFence.Firewall;

public interface IAuditPolCommandRunner
{
    AuditPolCommandResult Run(string args);
}
