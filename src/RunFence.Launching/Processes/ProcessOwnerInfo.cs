namespace RunFence.Launching.Processes;

public readonly record struct ProcessOwnerInfo(
    ProcessOwnerMatch Match,
    string? OwnerSid);
