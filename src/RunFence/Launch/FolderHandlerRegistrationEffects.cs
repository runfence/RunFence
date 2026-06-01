namespace RunFence.Launch;

public class FolderHandlerRegistrationEffects(string accountSid, string launcherPath)
{
    public string AccountSid { get; } = accountSid;
    public string LauncherPath { get; } = launcherPath;
    public bool AccountGrantApplied { get; set; }
    public bool AccountTraverseApplied { get; set; }
    public bool LowIntegrityGrantApplied { get; set; }
    public bool LowIntegrityTraverseApplied { get; set; }
    public FolderHandlerRegistrationChangeSet? RegistrationChangeSet { get; set; }
}
