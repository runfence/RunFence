namespace RunFence.Firewall;

public sealed record BlockedConnectionEventRecord(
    string DestAddress,
    int DestPort,
    DateTime TimeCreatedUtc);
