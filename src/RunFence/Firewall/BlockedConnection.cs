namespace RunFence.Firewall;

public record BlockedConnection(
    string DestAddress,
    int DestPort,
    DateTime TimeStamp);