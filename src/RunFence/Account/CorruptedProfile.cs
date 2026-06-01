namespace RunFence.Account;

public sealed record CorruptedProfile(string Sid, string OriginalPath, string TempPath);
