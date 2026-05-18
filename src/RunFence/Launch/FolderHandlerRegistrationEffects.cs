namespace RunFence.Launch;

public class FolderHandlerRegistrationEffects(string accountSid, string launcherPath)
{
    public string AccountSid { get; } = accountSid;
    public string LauncherPath { get; } = launcherPath;
    public bool RegistryWritten { get; set; }
    public bool AccountGrantApplied { get; set; }
    public bool AccountTraverseApplied { get; set; }
    public bool LowIntegrityGrantApplied { get; set; }
    public bool LowIntegrityTraverseApplied { get; set; }
    public bool RunOnceWritten { get; set; }
    public bool SidTracked { get; set; }
}
