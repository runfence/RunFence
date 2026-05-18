namespace RunFence.Firewall;

public sealed record EventLogRecordSnapshot(
    IReadOnlyList<object?> Properties,
    DateTime? TimeCreated);
